using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;

namespace Laraue.Apps.Storage.IntegrationTests;

/// <summary>
/// Download links from the test bucket (<c>file://</c>). What B2 does with a presigned link - content type,
/// disposition, ranges - is B2's; the link itself is checked in <see cref="S3FileBucketTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class GetDownloadUrlTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task GetDownloadUrl_ShouldReturnShortLink_WhenFileIsOriginal()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        Assert.Equal("notes.txt", location.File.FileName);
        Assert.Equal(5, location.File.Size);
        Assert.InRange(location.ExpiresAt.ToDateTime() - DateTime.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
        Assert.Equal("hello", await File.ReadAllTextAsync(new Uri(location.Url).LocalPath));
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnSameLinkAllDay_WhenFileIsPreview()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var first = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = uploaded.Preview.Id });
        var second = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = uploaded.Preview.Id });

        Assert.Equal(first.Url, second.Url);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(2), first.ExpiresAt.ToDateTime());
        Assert.EndsWith($"previews/{Guid.Parse(uploaded.Preview.Id):N}.webp", first.Url);
        Assert.Equal(new Uri(first.Url).LocalPath, Path.Combine(host.BucketDirectory, "previews", $"{Guid.Parse(uploaded.Preview.Id):N}.webp"));
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnNotFound_WhenFileIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.GetDownloadUrlAsync(
            new GetDownloadUrlRequest { FileId = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetFile_ShouldReturnDetails_WhenFileExists()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");

        var file = await client.GetFileAsync(new GetFileRequest { FileId = fileId });

        Assert.Equal("notes.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(5, file.Size);
    }

    private static async Task<string> Upload(StorageService.StorageServiceClient client, string text)
    {
        var response = await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text(text)));
        return response.Uploaded.File.Id;
    }
}
