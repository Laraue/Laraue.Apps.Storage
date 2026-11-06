using System.Net;
using System.Net.Http.Headers;
using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Links;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class GetDownloadUrlTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task GetDownloadUrl_ShouldReturnInlineLink_WhenAttachmentIsNotAsked()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });
        var response = await host.PublicHost.CreateClient().GetAsync(location.Url);

        Assert.NotNull(location.ExpiresAt);
        Assert.Equal("notes.txt", location.File.FileName);
        Assert.Equal(5, location.File.Size);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox; default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldNameTheDownload_WhenAttachmentIsAsked()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest
        {
            FileId = fileId,
            Attachment = true,
            DownloadFileName = "отчёт.txt",
        });
        var response = await host.PublicHost.CreateClient().GetAsync(location.Url);

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("отчёт.txt", disposition.FileNameStar);
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnLinkServingRanges_WhenRangeIsAsked()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });
        var request = new HttpRequestMessage(HttpMethod.Get, location.Url);
        request.Headers.Range = new RangeHeaderValue(2, 5);
        var response = await host.PublicHost.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnSameCacheableLink_WhenPreviewIsAskedTwice()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var first = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = uploaded.Preview.Id });
        var second = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = uploaded.Preview.Id });
        var response = await host.PublicHost.CreateClient().GetAsync(first.Url);

        Assert.Equal(first.Url, second.Url);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(2), first.ExpiresAt.ToDateTime());
        Assert.Equal("image/webp", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.MaxAge > TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task PublicHost_ShouldReturnNotFound_WhenSignatureIsWrong()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");
        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        var response = await host.PublicHost.CreateClient().GetAsync(location.Url[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicHost_ShouldReturnNotFound_WhenSignedValueIsChanged()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "<script>alert(1)</script>");
        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        var response = await host.PublicHost.CreateClient().GetAsync(location.Url.Replace("ct=text%2Fplain", "ct=text%2Fhtml"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicHost_ShouldReturnNotFound_WhenLinkIsExpired()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("hello")))).Uploaded;
        var key = $"files/{Guid.Parse(uploaded.File.Id):N}";
        var signer = host.Services.GetRequiredService<FileLinkSigner>();

        var url = signer.CreateUrl(
            "http://localhost",
            LocalFileBucket.OriginalsArea,
            key,
            new FileDownload("text/plain", "inline", DateTime.UtcNow.AddMinutes(-1)));
        var response = await host.PublicHost.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicHost_ShouldReturnNotFound_WhenSignedKeyLeavesTheDirectory()
    {
        var signer = host.Services.GetRequiredService<FileLinkSigner>();

        var url = signer.CreateUrl(
            "http://localhost",
            LocalFileBucket.PreviewsArea,
            "../../../../etc/passwd",
            new FileDownload("text/plain", "inline", DateTime.UtcNow.AddMinutes(5)));
        var response = await host.PublicHost.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InternalHost_ShouldNotServeFiles_WhenLinkIsValid()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "hello");
        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });

        var response = await host.CreateClient().GetAsync(location.Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
