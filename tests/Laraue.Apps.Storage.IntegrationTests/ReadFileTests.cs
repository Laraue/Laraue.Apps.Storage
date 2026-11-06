using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class ReadFileTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task ReadFile_ShouldReturnRange_WhenRangeIsGiven()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var (header, data) = await client.ReadAll(new ReadFileRequest { FileId = fileId, RangeFrom = 2, RangeTo = 5 });

        Assert.True(header.Partial);
        Assert.Equal(2, header.From);
        Assert.Equal(5, header.To);
        Assert.Equal(10, header.TotalLength);
        Assert.Equal("2345", System.Text.Encoding.UTF8.GetString(data));
    }

    [Fact]
    public async Task ReadFile_ShouldReturnTail_WhenRangeHasNoEnd()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var (header, data) = await client.ReadAll(new ReadFileRequest { FileId = fileId, RangeFrom = 7 });

        Assert.Equal(9, header.To);
        Assert.Equal("789", System.Text.Encoding.UTF8.GetString(data));
    }

    [Fact]
    public async Task ReadFile_ShouldReturnLastBytes_WhenSuffixLengthIsGiven()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var (header, data) = await client.ReadAll(new ReadFileRequest { FileId = fileId, SuffixLength = 4 });

        Assert.Equal(6, header.From);
        Assert.Equal("6789", System.Text.Encoding.UTF8.GetString(data));
    }

    [Fact]
    public async Task ReadFile_ShouldReturnOutOfRange_WhenRangeStartsPastEnd()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var fileId = await Upload(client, "0123456789");

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.ReadAll(new ReadFileRequest { FileId = fileId, RangeFrom = 10 }));

        Assert.Equal(StatusCode.OutOfRange, exception.StatusCode);
        Assert.Equal("10", exception.Trailers.GetValue("total-length"));
    }

    [Fact]
    public async Task ReadFile_ShouldStreamInChunks_WhenFileIsLarge()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var content = new byte[300_000];
        new Random(1).NextBytes(content);
        var uploaded = await client.UploadFileAsync(TestFiles.Upload(content, "data.bin", "application/octet-stream"));

        var (_, data) = await client.ReadAll(new ReadFileRequest { FileId = uploaded.Uploaded.File.Id });

        Assert.Equal(content, data);
    }

    [Fact]
    public async Task ReadFile_ShouldReturnNotFound_WhenFileIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.ReadAll(new ReadFileRequest { FileId = Guid.NewGuid().ToString() }));

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
