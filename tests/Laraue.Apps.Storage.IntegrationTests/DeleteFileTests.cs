using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class DeleteFileTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task DeleteFile_ShouldDeleteFileAndPreview_WhenLastReferenceIsDropped()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var response = await client.DeleteFileAsync(new DeleteFileRequest { FileId = uploaded.File.Id });

        Assert.True(response.Deleted);
        Assert.Equal(0, response.OwnerUsageBytes);
        Assert.Equal(0, await testScope.Database.StoredFiles.CountAsync());
        Assert.Empty(Directory.EnumerateFiles(host.BucketDirectory, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(host.PreviewsDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(0, (await client.GetUsageAsync(new GetUsageRequest { Owner = "organization:1" })).UsageBytes);
    }

    [Fact]
    public async Task DeleteFile_ShouldKeepFile_WhenAnotherReferenceIsLeft()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload(TestFiles.Text("shared"));
        var uploaded = (await client.UploadFileAsync(request)).Uploaded;
        await client.UploadFileAsync(request);

        var first = await client.DeleteFileAsync(new DeleteFileRequest { FileId = uploaded.File.Id });

        Assert.False(first.Deleted);
        Assert.Equal(6, first.OwnerUsageBytes);
        var (_, data) = await client.ReadAll(new ReadFileRequest { FileId = uploaded.File.Id });
        Assert.Equal("shared", System.Text.Encoding.UTF8.GetString(data));

        var second = await client.DeleteFileAsync(new DeleteFileRequest { FileId = uploaded.File.Id });

        Assert.True(second.Deleted);
        Assert.Equal(0, second.OwnerUsageBytes);
    }

    [Fact]
    public async Task DeleteFile_ShouldDoNothing_WhenFileIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var response = await client.DeleteFileAsync(new DeleteFileRequest { FileId = Guid.NewGuid().ToString() });

        Assert.False(response.Deleted);
    }

    [Fact]
    public async Task DeleteFile_ShouldKeepOriginal_WhenPreviewIdIsGiven()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var response = await client.DeleteFileAsync(new DeleteFileRequest { FileId = uploaded.Preview.Id });

        Assert.False(response.Deleted);
        Assert.Equal(2, await testScope.Database.StoredFiles.CountAsync());
    }
}
