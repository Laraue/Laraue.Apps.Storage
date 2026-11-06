using Google.Protobuf;
using Grpc.Core;
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
    /// Reads a whole <c>ReadFile</c> stream: its header and the data concatenated.
    /// </summary>
    public static async Task<(ReadFileHeader Header, byte[] Data)> ReadAll(
        this StorageService.StorageServiceClient client,
        ReadFileRequest request)
    {
        using var call = client.ReadFile(request);
        ReadFileHeader? header = null;
        using var data = new MemoryStream();

        await foreach (var message in call.ResponseStream.ReadAllAsync())
        {
            if (message.PartCase == ReadFileResponse.PartOneofCase.Header)
                header = message.Header;
            else
                data.Write(message.Data.Span);
        }

        return (header!, data.ToArray());
    }
}
