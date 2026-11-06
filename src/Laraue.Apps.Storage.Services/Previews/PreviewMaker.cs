using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Laraue.Apps.Storage.Services.Previews;

public interface IPreviewMaker
{
    /// <summary>
    /// A WebP of the image at most <see cref="PreviewMaker.MaxSide"/> px on its long side (the first frame of an
    /// animation, turned upright by its EXIF orientation, never scaled up), or null when the content isn't an image
    /// Skia reads (JPEG, PNG, GIF, WebP, BMP...) or is too big to decode.
    /// </summary>
    byte[]? TryMake(byte[] content);
}

public sealed class PreviewMaker(ILogger<PreviewMaker> logger) : IPreviewMaker
{
    public const int MaxSide = 640;
    public const string ContentType = "image/webp";

    private const int WebpQuality = 80;

    /// <summary>
    /// The largest image decoded at all: 40 megapixels. A JPEG is decoded at a reduced scale (at most 4 bytes per
    /// pixel of that), other formats at full size, so this caps one preview's memory at about 160 MB.
    /// </summary>
    private const long MaxPixels = 40_000_000;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public byte[]? TryMake(byte[] content)
    {
        try
        {
            using var data = SKData.CreateCopy(content);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                return null;

            var size = codec.Info.Size;
            if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > MaxPixels)
                return null;

            using var decoded = Decode(codec);
            if (decoded is null)
                return null;

            using var resized = ResizeToFit(decoded);
            using var upright = Orient(resized ?? decoded, codec.EncodedOrigin);
            using var image = SKImage.FromBitmap(upright ?? resized ?? decoded);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);

            return encoded?.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            logger.LogWarning(e, "No preview made: the image couldn't be decoded");
            return null;
        }
    }

    /// <summary>
    /// Decodes the first frame, at the smallest scale the codec offers that is still at least the preview size
    /// (JPEG decodes at 1/2, 1/4, 1/8 straight away; other formats only at full size).
    /// </summary>
    private static SKBitmap? Decode(SKCodec codec)
    {
        var size = codec.Info.Size;
        var scale = Math.Min(1f, (float)MaxSide / Math.Max(size.Width, size.Height));
        var scaled = codec.GetScaledDimensions(scale);
        if (scaled.Width < 1 || scaled.Height < 1 || Math.Max(scaled.Width, scaled.Height) < Math.Min(MaxSide, Math.Max(size.Width, size.Height)))
            scaled = size;

        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());

        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        return null;
    }

    /// <summary>
    /// The bitmap scaled to fit <see cref="MaxSide"/>, or null when it already does.
    /// </summary>
    private static SKBitmap? ResizeToFit(SKBitmap bitmap)
    {
        var longSide = Math.Max(bitmap.Width, bitmap.Height);
        if (longSide <= MaxSide)
            return null;

        var ratio = (double)MaxSide / longSide;
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(bitmap.Width * ratio)),
            Math.Max(1, (int)Math.Round(bitmap.Height * ratio)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        return bitmap.Resize(info, Sampling);
    }

    /// <summary>
    /// The bitmap turned upright by the image's EXIF orientation (phone photos), or null when it already is.
    /// </summary>
    private static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return null;

        var swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsSides ? bitmap.Height : bitmap.Width;
        var height = swapsSides ? bitmap.Width : bitmap.Height;

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // upside down
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // flipped
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // needs 90 degrees clockwise
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transversed
                canvas.Translate(width, height);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom: // needs 90 degrees counter-clockwise
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }

        using var source = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(source, 0, 0, Sampling);

        return result;
    }
}
