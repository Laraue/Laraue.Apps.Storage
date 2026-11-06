using System.Net;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class PublicHostTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task GetFile_ShouldStreamPreviewWithStoredHeaders_WhenPreviewExists()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var response = await host.PublicHost.CreateClient().GetAsync(PathOf(uploaded.Preview.Url));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl!.MaxAge);
        Assert.True(response.Headers.CacheControl.Public);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox; default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(uploaded.Preview.Size, response.Content.Headers.ContentLength);
        Assert.Equal(await host.Download(client, uploaded.Preview.Id), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetFile_ShouldServeDocumentAsNamedAttachment_WhenOriginalExists()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("%PDF-1.4"), "отчёт.pdf", "application/pdf"))).Uploaded;

        var response = await host.PublicHost.CreateClient().GetAsync(PathOf(uploaded.File.Url));

        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("отчёт.pdf", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("%PDF-1.4", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetFile_ShouldReturnNotFound_WhenFileIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("bye")))).Uploaded;
        await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = uploaded.ReferenceId });

        var response = await host.PublicHost.CreateClient().GetAsync(PathOf(uploaded.File.Url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/files/0123456789abcdef0123456789abcdef")]
    [InlineData("/previews/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("/files/0123456789abcdef0123456789abcdef.headers.json")]
    [InlineData("/files/NOT-A-FILE-ID")]
    [InlineData("/previews/0123456789abcdef0123456789abcdef")]
    [InlineData("/other/0123456789abcdef0123456789abcdef")]
    [InlineData("/files/..%2F..%2Fetc%2Fpasswd")]
    public async Task GetFile_ShouldReturnNotFound_WhenKeyIsUnknownOrNotAFile(string path)
    {
        using var testScope = host.CreateTestScope();

        var response = await host.PublicHost.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_ShouldCountPublicReads_WhenFilesAreRequested()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("count me")))).Uploaded;
        var http = host.PublicHost.CreateClient();
        await http.GetAsync(PathOf(uploaded.File.Url));

        var body = await http.GetStringAsync("/_metrics");

        Assert.Contains("storage_public_reads_total{otel_scope_name=\"Laraue.Apps.Storage\",result=\"served\"}", body);
        Assert.Contains("storage_public_reads_total{otel_scope_name=\"Laraue.Apps.Storage\",result=\"not_found\"}", body);
    }

    /// <summary>
    /// The path of a file's URL, which in tests is a <c>file://</c> URL into the bucket folder: what follows the
    /// folder is the key, the path the public host serves it at.
    /// </summary>
    private string PathOf(string url)
    {
        var baseUrl = new Uri(host.BucketDirectory).AbsoluteUri;
        return url[baseUrl.Length..];
    }
}
