using CityPortal.Identity;
using CityPortal.Media;
using SkiaSharp;

namespace CityPortal.IntegrationTests;

public sealed class MediaBoundaryTests
{
    [Theory]
    [InlineData("image/png", "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAACGFjVEwAAAACAAAAAPONk3AAAAAaZmNUTAAAAAAAAAAIAAAACAAAAAAAAAAAAAEACgAA8k66YgAAABZJREFUeJxj/M/A8J8BD2DCJzl8FAAAElsCDh9KSV0AAAAaZmNUTAAAAAEAAAAIAAAACAAAAAAAAAAAAAEACgAAaT1QtgAAABpmZEFUAAAAAnicY2Rg+P+fAQ9gwic5fBQAABBdAg7EQ+AwAAAAAElFTkSuQmCC")]
    [InlineData("image/webp", "UklGRsQAAABXRUJQVlA4WAoAAAACAAAABwAABwAAQU5JTQYAAAAAAAAAAABBTk1GSgAAAAAAAAAAAAcAAAcAAGQAAAJWUDggMgAAADABAJ0BKggACAABQCYloAADcAD+8ut///mwP/bz/wR6Af//0uD//pcH//S4P/SkAAAAQU5NRkYAAAAAAAAAAAAHAAAHAABkAAAAVlA4IC4AAAA0AQCdASoIAAgAAAAmJaAAA3AA/vtV4///S4P/+lwf/9Lg/9Lg//rV5Vesq6AA")]
    [InlineData("image/webp", "UklGRnYAAABXRUJQVlA4WAoAAAACAAAABwAABwAAQU5JTQYAAAAAAAAAAABBTk1GSgAAAAAAAAAAAAcAAAcAAGQAAAJWUDggMgAAADABAJ0BKggACAABQCYloAADcAD+8ut///mwP/bz/wR6Af//0uD//pcH//S4P/SkAAAA")]
    public async Task Process_RealAnimatedPngAndWebp_RejectsAnimationAsync(string mime, string fixture)
    {
        AccountOperationException error = await Assert.ThrowsAsync<AccountOperationException>(() => new MediaImageProcessor().ProcessAsync(new MemoryStream(Convert.FromBase64String(fixture)), mime, CancellationToken.None));
        Assert.Equal(415, error.Status);
        Assert.Equal("animated_image", error.Code);
    }

    [Fact]
    public async Task Process_OverFortyMegapixels_RejectsBeforePixelAllocationAsync()
    {
        byte[] header = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAG1gAABdwCAYAAAC1xFagAAAAFklEQVR4nGP8z8DwnwEPYMInOXwUAAASWwIOH0pJXQAAAABJRU5ErkJggg==");
        AccountOperationException error = await Assert.ThrowsAsync<AccountOperationException>(() => new MediaImageProcessor().ProcessAsync(new MemoryStream(header), "image/png", CancellationToken.None));
        Assert.Equal("image_dimensions", error.Code);
    }

    [Fact]
    public async Task Process_ExactlyTenMebibytes_AcceptsValidJpegWithTrailingPaddingAsync()
    {
        byte[] bytes = new byte[MediaImageProcessor.MaximumBytes];
        MediaProcessorTests.Image(SKEncodedImageFormat.Jpeg).CopyTo(bytes, 0);
        IReadOnlyList<ProcessedImage> images = await new MediaImageProcessor().ProcessAsync(new MemoryStream(bytes), "image/jpeg", CancellationToken.None);
        Assert.Equal(4, images.Count);
        Assert.Equal(32, images[0].Width);
    }
}
