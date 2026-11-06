using Google.Protobuf;
using Laraue.Apps.Storage.Internal.Contracts;
using SkiaSharp;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

public static class TestFiles
{
    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    /// <summary>
    /// A JPEG whose EXIF says how to turn it upright (6: rotate 90 degrees clockwise), the way phones save photos.
    /// </summary>
    public static byte[] JpegWithOrientation(int width, int height, ushort orientation)
    {
        var jpeg = Jpeg(width, height);
        byte[] tiff =
        [
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // big-endian TIFF header, IFD0 at 8
            0x00, 0x01, // one entry
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, // no next IFD
        ];
        byte[] exifHeader = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00];
        var segmentLength = 2 + exifHeader.Length + tiff.Length;

        return [0xFF, 0xD8, 0xFF, 0xE1, (byte)(segmentLength >> 8), (byte)segmentLength, .. exifHeader, .. tiff, .. jpeg[2..]];
    }

    public static SKSizeI Identify(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);

        return codec.Info.Size;
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(30, 120, 200));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);

        return data.ToArray();
    }

    public static byte[] Text(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    public static Owner Organization(long organizationId = 1) => new() { OrganizationId = organizationId };

    public static UploadFileRequest Upload(
        byte[] content,
        string fileName = "notes.txt",
        string contentType = "text/plain",
        Owner? owner = null)
    {
        return new UploadFileRequest
        {
            Owner = owner ?? Organization(),
            UserId = Guid.NewGuid().ToString(),
            FileName = fileName,
            ContentType = contentType,
            Content = ByteString.CopyFrom(content),
        };
    }

    /// <summary>
    /// Asks for the file's download link and downloads it from the public host, the way a client follows the
    /// redirect.
    /// </summary>
    public static async Task<byte[]> Download(
        this InternalApiTestHost host,
        StorageService.StorageServiceClient client,
        string fileId)
    {
        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        return await host.PublicHost.CreateClient().GetByteArrayAsync(location.Url);
    }
}
