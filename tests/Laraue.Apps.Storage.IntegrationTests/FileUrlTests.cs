using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;

namespace Laraue.Apps.Storage.IntegrationTests;

/// <summary>
/// Files' public URLs and the headers objects are stored with. What the bucket and Cloudflare do with them is theirs;
/// the upload request itself is checked in <see cref="S3FileBucketTests"/>.
/// </summary>
[Collection("IntegrationTest")]
public class FileUrlTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task UploadFile_ShouldReturnPublicUrls_WhenImageIsUploaded()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var baseUrl = new Uri(host.BucketDirectory).AbsoluteUri;
        Assert.Equal($"{baseUrl}/{Guid.Parse(uploaded.File.Id):N}", uploaded.File.Url);
        Assert.Equal($"{baseUrl}/{Guid.Parse(uploaded.Preview.Id):N}", uploaded.Preview.Url);
        Assert.True(File.Exists(new Uri(uploaded.Preview.Url).LocalPath));
    }

    [Fact]
    public async Task GetFile_ShouldReturnSameUrl_WhenFileIsUploaded()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("hello")))).Uploaded;

        var file = await client.GetFileAsync(new GetFileRequest { FileId = uploaded.File.Id });

        Assert.Equal("notes.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(5, file.Size);
        Assert.Equal(uploaded.File.Url, file.Url);
        Assert.Equal("hello", await File.ReadAllTextAsync(new Uri(file.Url).LocalPath));
    }

    [Fact]
    public async Task UploadFile_ShouldStoreImageInlineAndCachedForADay_WhenImageIsUploaded()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var original = host.Bucket.Headers[$"{Guid.Parse(uploaded.File.Id):N}"];
        var preview = host.Bucket.Headers[$"{Guid.Parse(uploaded.Preview.Id):N}"];
        Assert.Equal("image/png", original.ContentType);
        Assert.Equal("inline", original.ContentDisposition);
        Assert.Equal("public, max-age=86400", original.CacheControl);
        Assert.Equal("image/webp", preview.ContentType);
        Assert.Equal("inline", preview.ContentDisposition);
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("drawing.svg", "image/svg+xml")]
    public async Task UploadFile_ShouldStoreAsNamedAttachment_WhenFileIsNotShown(string fileName, string contentType)
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("content"), fileName, contentType))).Uploaded;

        var headers = host.Bucket.Headers[$"{Guid.Parse(uploaded.File.Id):N}"];
        Assert.Equal($"attachment; filename={fileName}; filename*=UTF-8''{fileName}", headers.ContentDisposition);
    }

    [Fact]
    public async Task UploadFile_ShouldKeepNonAsciiName_WhenFileIsAttachment()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("content"), "отчёт.pdf", "application/pdf"))).Uploaded;

        var disposition = System.Net.Http.Headers.ContentDispositionHeaderValue.Parse(
            host.Bucket.Headers[$"{Guid.Parse(uploaded.File.Id):N}"].ContentDisposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("отчёт.pdf", disposition.FileNameStar);
    }

    [Fact]
    public async Task GetFile_ShouldReturnNotFound_WhenFileIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.GetFileAsync(
            new GetFileRequest { FileId = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }
}
