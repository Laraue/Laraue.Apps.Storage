using System.Net;
using System.Net.Http.Headers;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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
        var response = await host.CreateClient().GetAsync(location.Url);

        Assert.Equal(GetDownloadUrlResponse.LocationOneofCase.Url, location.LocationCase);
        Assert.NotNull(location.ExpiresAt);
        Assert.Equal("notes.txt", location.File.FileName);
        Assert.Equal(5, location.File.Size);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
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
        var response = await host.CreateClient().GetAsync(location.Url);

        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("отчёт.txt", disposition.FileNameStar);
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnLinkServingRanges_WhenFileIsOriginal()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = fileId });
        var request = new HttpRequestMessage(HttpMethod.Get, location.Url);
        request.Headers.Range = new RangeHeaderValue(2, 5);
        var response = await host.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("2345", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDownloadUrl_ShouldReturnInternalPath_WhenPreviewIsServedByNginx()
    {
        // Production: no local-files endpoint, so a preview is a path under the previews volume for nginx.
        using var productionLike = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            config => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalFilesBaseUrl"] = "" })));
        using var testScope = host.CreateTestScope();
        var client = new StorageService.StorageServiceClient(
            global::Grpc.Net.Client.GrpcChannel
                .ForAddress(productionLike.Server.BaseAddress, new global::Grpc.Net.Client.GrpcChannelOptions { HttpClient = productionLike.CreateDefaultClient() })
                .Intercept(new ServiceIdInterceptor(ServiceId.LaraueBoards)));
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var location = await client.GetDownloadUrlAsync(new GetDownloadUrlRequest { FileId = uploaded.Preview.Id });

        Assert.Equal(GetDownloadUrlResponse.LocationOneofCase.InternalPath, location.LocationCase);
        Assert.Null(location.ExpiresAt);
        Assert.EndsWith($"{Guid.Parse(uploaded.Preview.Id):N}.webp", location.InternalPath);
        Assert.True(File.Exists(Path.Combine(host.PreviewsDirectory, location.InternalPath)));
        Assert.Equal(HttpStatusCode.NotFound, (await productionLike.CreateClient().GetAsync("/_local-files/previews/" + location.InternalPath)).StatusCode);
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
    public async Task LocalFiles_ShouldReturnNotFound_WhenKeyLeavesTheDirectory()
    {
        var response = await host.CreateClient().GetAsync("/_local-files/originals/..%2F..%2Fetc%2Fpasswd");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
