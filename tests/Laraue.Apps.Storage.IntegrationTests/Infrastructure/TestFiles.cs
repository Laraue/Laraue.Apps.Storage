using Google.Protobuf;
using Laraue.Apps.Storage.Internal.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

public static class TestFiles
{
    public static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 120, 200));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);

        return stream.ToArray();
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
    /// Asks for the file's download link and downloads it from the test host's local-files endpoint, the way a
    /// client follows the redirect.
    /// </summary>
    public static async Task<byte[]> Download(
        this InternalApiTestHost host,
        StorageService.StorageServiceClient client,
        string fileId)
    {
        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        return await host.CreateClient().GetByteArrayAsync(location.Url);
    }
}
