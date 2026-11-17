using System.Text;
using CityPortal.Identity;
using CityPortal.Media;
using SkiaSharp;

namespace CityPortal.IntegrationTests;

public sealed class MediaProcessorTests
{
    [Theory]
    [InlineData("image/png", SKEncodedImageFormat.Png)]
    [InlineData("image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("image/webp", SKEncodedImageFormat.Webp)]
    public async Task Process_ValidFormats_EmitsBoundedWebpWithoutMetadataAsync(string mime, SKEncodedImageFormat format)
    {
        byte[] input = Image(format, 800, 400);
        IReadOnlyList<ProcessedImage> outputs = await new MediaImageProcessor().ProcessAsync(new MemoryStream(input), mime, CancellationToken.None);
        Assert.Equal(["master", "480", "1280", "2560"], outputs.Select(output => output.Variant));
        Assert.Equal([(800, 400), (480, 240), (800, 400), (800, 400)], outputs.Select(output => (output.Width, output.Height)));
        foreach (ProcessedImage output in outputs)
        {
            using var codec = SKCodec.Create(new MemoryStream(output.Bytes));
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.True(codec.FrameCount <= 1);
            Assert.Equal(output.Width, codec.Info.Width);
            Assert.DoesNotContain("EXIF", Encoding.Latin1.GetString(output.Bytes), StringComparison.Ordinal);
            Assert.DoesNotContain("XMP ", Encoding.Latin1.GetString(output.Bytes), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("image/svg+xml", "<svg></svg>")]
    [InlineData("image/gif", "GIF89a")]
    [InlineData("image/png", "not an image")]
    public async Task Process_InvalidAndUnsupportedContent_RejectsAsync(string mime, string bytes)
    {
        AccountOperationException error = await Assert.ThrowsAsync<AccountOperationException>(() => new MediaImageProcessor().ProcessAsync(new MemoryStream(Encoding.UTF8.GetBytes(bytes)), mime, CancellationToken.None));
        Assert.Equal(415, error.Status);
    }

    [Fact]
    public async Task Process_MismatchedMimeAndOversizedInput_RejectsBeforeStorageAsync()
    {
        var processor = new MediaImageProcessor();
        AccountOperationException mismatch = await Assert.ThrowsAsync<AccountOperationException>(() => processor.ProcessAsync(new MemoryStream(Image(SKEncodedImageFormat.Png)), "image/jpeg", CancellationToken.None));
        Assert.Equal(415, mismatch.Status);
        AccountOperationException oversize = await Assert.ThrowsAsync<AccountOperationException>(() => processor.ProcessAsync(new MemoryStream(new byte[MediaImageProcessor.MaximumBytes + 1]), "image/png", CancellationToken.None));
        Assert.Equal(413, oversize.Status);
    }

    [Fact]
    public async Task Process_ExifOrientation_RotatesAndStripsSourceMetadataAsync()
    {
        byte[] jpeg = Image(SKEncodedImageFormat.Jpeg, 40, 20);
        byte[] exif = [0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        using var stream = new MemoryStream();
        stream.Write(jpeg.AsSpan(0, 2));
        stream.Write([255, 225, 0, (byte)(exif.Length + 2)]);
        stream.Write(exif);
        stream.Write(jpeg.AsSpan(2));
        stream.Position = 0;
        IReadOnlyList<ProcessedImage> outputs = await new MediaImageProcessor().ProcessAsync(stream, "image/jpeg", CancellationToken.None);
        Assert.Equal((20, 40), (outputs[0].Width, outputs[0].Height));
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(outputs[0].Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_TransparentPng_PreservesAlphaAsync()
    {
        IReadOnlyList<ProcessedImage> output = await new MediaImageProcessor().ProcessAsync(new MemoryStream(Image(SKEncodedImageFormat.Png)), "image/png", CancellationToken.None);
        using var bitmap = SKBitmap.Decode(output[0].Bytes);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.True(bitmap.GetPixel(15, 15).Alpha > 200);
    }

    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 32, int height = 32)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.ForestGreen };
        canvas.DrawRect(new SKRect(8, 8, width, height), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, 95);
        return data.ToArray();
    }
}
