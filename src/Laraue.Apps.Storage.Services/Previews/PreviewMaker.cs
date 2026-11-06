using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;

namespace Laraue.Apps.Storage.Services.Previews;

public interface IPreviewMaker
{
    /// <summary>
    /// A WebP of the image at most <see cref="PreviewMaker.MaxSide"/> px on its long side (the first frame of an
    /// animation), or null when the content isn't an image ImageSharp reads (JPEG, PNG, GIF, WebP...) or is too
    /// big to decode within the memory limit.
    /// </summary>
    byte[]? TryMake(byte[] content);
}

public sealed class PreviewMaker(ILogger<PreviewMaker> logger) : IPreviewMaker
{
    public const int MaxSide = 640;
    public const string ContentType = "image/webp";

    /// <summary>
    /// A decoded image takes 4 bytes per pixel; 40 megapixels is 160 MB, the most one preview may take.
    /// </summary>
    private const long MaxPixels = 40_000_000;

    // The server has little memory to spare: keep ImageSharp's buffer pool small and cap any single image.
    private static readonly Configuration ImageConfiguration = CreateConfiguration();

    private static readonly WebpEncoder Encoder = new()
    {
        Quality = 80,
        FileFormat = WebpFileFormatType.Lossy,
    };

    public byte[]? TryMake(byte[] content)
    {
        try
        {
            var info = Image.Identify(new DecoderOptions { Configuration = ImageConfiguration }, content);
            if ((long)info.Width * info.Height > MaxPixels)
                return null;

            var isLarger = info.Width > MaxSide || info.Height > MaxSide;
            var decoderOptions = new DecoderOptions
            {
                Configuration = ImageConfiguration,
                MaxFrames = 1,
                // Decodes straight at the preview size (JPEG scales while decoding; other formats are resized
                // right after). Only for a larger image: a smaller one would be scaled up.
                TargetSize = isLarger ? new Size(MaxSide, MaxSide) : null,
            };

            using var image = Image.Load(decoderOptions, content);
            if (image.Width > MaxSide || image.Height > MaxSide)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxSide, MaxSide),
                    Mode = ResizeMode.Max,
                }));
            }

            using var output = new MemoryStream();
            image.SaveAsWebp(output, Encoder);

            return output.ToArray();
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (Exception e) when (e is ImageFormatException or InvalidImageContentException or InvalidMemoryOperationException)
        {
            logger.LogWarning(e, "No preview made: the image couldn't be decoded");
            return null;
        }
    }

    private static Configuration CreateConfiguration()
    {
        var configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            MaximumPoolSizeMegabytes = 8,
            AllocationLimitMegabytes = 192,
        });
        configuration.PreferContiguousImageBuffers = false;

        return configuration;
    }
}
