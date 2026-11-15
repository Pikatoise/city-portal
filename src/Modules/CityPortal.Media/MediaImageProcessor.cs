using System.Buffers.Binary;
using CityPortal.Identity;
using SkiaSharp;

namespace CityPortal.Media;

public sealed class MediaImageProcessor
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly SemaphoreSlim _capacity = new(1);

    public async Task<IReadOnlyList<ProcessedImage>> ProcessAsync(Stream source, string contentType, CancellationToken cancellationToken)
    {
        await _capacity.WaitAsync(cancellationToken);
        try
        {
            using var input = new MemoryStream();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (input.Length + count > MaximumBytes)
                    throw new AccountOperationException(413, "image_too_large", "Images must not exceed 10 MiB.");
                await input.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            byte[] encoded = input.ToArray();
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                throw Invalid();
            string expected = codec.EncodedFormat switch { SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png", SKEncodedImageFormat.Webp => "image/webp", _ => "" };
            if (expected.Length == 0 || !string.Equals(expected, contentType, StringComparison.OrdinalIgnoreCase))
                throw Invalid();
            if (codec.FrameCount > 1 || AnimatedPng(encoded) || AnimatedWebp(encoded))
                throw new AccountOperationException(415, "animated_image", "Animated images are not supported.");
            SKImageInfo info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 40_000_000)
                throw new AccountOperationException(400, "image_dimensions", "Images must not exceed 40 megapixels.");
            SKBitmap orientedBitmap;
            using (var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul)))
            {
                SKCodecResult result = codec.GetPixels(decoded.Info, decoded.GetPixels());
                if (result != SKCodecResult.Success)
                    throw Invalid();
                orientedBitmap = Orient(decoded, codec.EncodedOrigin);
            }
            using SKBitmap oriented = orientedBitmap;
            oriented.SetImmutable();
            using var master = SKImage.FromBitmap(oriented);
            var outputs = new List<ProcessedImage>();
            byte[]? masterBytes = null;
            foreach ((string variant, int edge) in new[] { ("master", int.MaxValue), ("480", 480), ("1280", 1280), ("2560", 2560) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                double scale = Math.Min(1d, (double)edge / Math.Max(oriented.Width, oriented.Height));
                int width = Math.Max(1, (int)Math.Round(oriented.Width * scale));
                int height = Math.Max(1, (int)Math.Round(oriented.Height * scale));
                if (width == oriented.Width && height == oriented.Height)
                {
                    if (masterBytes is null)
                    {
                        using SKData encodedMaster = master.Encode(SKEncodedImageFormat.Webp, 85);
                        masterBytes = encodedMaster.ToArray();
                    }
                    outputs.Add(new ProcessedImage(variant, width, height, masterBytes));
                    continue;
                }
                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.DrawImage(master, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                using SKImage image = surface.Snapshot();
                using SKData output = image.Encode(SKEncodedImageFormat.Webp, 85);
                outputs.Add(new ProcessedImage(variant, width, height, output.ToArray()));
            }
            return outputs;
        }
        finally
        {
            _capacity.Release();
        }
    }

    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        float w = bitmap.Width;
        float h = bitmap.Height;
        (float a, float b, float c, float d, float x, float y) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1, 0, 0, 1, w, 0),
            SKEncodedOrigin.BottomRight => (-1, 0, 0, -1, w, h),
            SKEncodedOrigin.BottomLeft => (1, 0, 0, -1, 0, h),
            SKEncodedOrigin.LeftTop => (0, 1, 1, 0, 0, 0),
            SKEncodedOrigin.RightTop => (0, -1, 1, 0, h, 0),
            SKEncodedOrigin.RightBottom => (0, -1, -1, 0, h, w),
            SKEncodedOrigin.LeftBottom => (0, 1, -1, 0, 0, w),
            _ => (1, 0, 0, 1, 0, 0)
        };
        canvas.SetMatrix(new SKMatrix { ScaleX = a, SkewX = b, SkewY = c, ScaleY = d, TransX = x, TransY = y, Persp2 = 1 });
        canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return output;
    }

    private static bool AnimatedPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return false;
        int offset = 8;
        while (offset <= bytes.Length - 12)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (bytes.Slice(offset + 4, 4).SequenceEqual("acTL"u8))
                return true;
            if (size > bytes.Length - offset - 12)
                break;
            offset += (int)size + 12;
        }
        return false;
    }

    private static bool AnimatedWebp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return false;
        int offset = 12;
        while (offset <= bytes.Length - 8)
        {
            ReadOnlySpan<byte> type = bytes.Slice(offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (size > bytes.Length - offset - 8)
                break;
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8) || type.SequenceEqual("VP8X"u8) && size > 0 && (bytes[offset + 8] & 2) != 0)
                return true;
            offset += (int)size + 8 + (int)(size & 1);
        }
        return false;
    }

    private static AccountOperationException Invalid() => new(415, "unsupported_image", "Upload a valid JPEG, PNG, or static WebP with its matching content type.");
}
