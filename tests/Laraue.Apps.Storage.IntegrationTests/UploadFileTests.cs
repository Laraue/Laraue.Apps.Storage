using Google.Protobuf;
using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using Domain = Laraue.Apps.Storage.DataAccess.Entities;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class UploadFileTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task UploadFile_ShouldStoreOriginalAndPreview_WhenFileIsImage()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var png = TestFiles.Png(1600, 800);

        var response = await client.UploadFileAsync(TestFiles.Upload(png, "photo.png", "image/png"));

        Assert.Equal(UploadFileResponse.ResultOneofCase.Uploaded, response.ResultCase);
        var uploaded = response.Uploaded;
        Assert.Equal("photo.png", uploaded.File.FileName);
        Assert.Equal("image/png", uploaded.File.ContentType);
        Assert.Equal(png.Length, uploaded.File.Size);
        Assert.False(uploaded.Deduplicated);

        Assert.NotNull(uploaded.Preview);
        Assert.Equal("image/webp", uploaded.Preview.ContentType);
        Assert.Equal("photo.webp", uploaded.Preview.FileName);
        Assert.Equal(png.Length + uploaded.Preview.Size, uploaded.OwnerUsageBytes);

        var (_, previewBytes) = await client.ReadAll(new ReadFileRequest { FileId = uploaded.Preview.Id });
        var previewInfo = Image.Identify(previewBytes);
        Assert.Equal(640, previewInfo.Width);
        Assert.Equal(320, previewInfo.Height);

        var (header, originalBytes) = await client.ReadAll(new ReadFileRequest { FileId = uploaded.File.Id });
        Assert.Equal(png, originalBytes);
        Assert.False(header.Partial);
        Assert.Equal(png.Length, header.TotalLength);
    }

    [Fact]
    public async Task UploadFile_ShouldKeepPreviewSize_WhenImageIsSmallerThanPreview()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var response = await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(100, 50), "small.png", "image/png"));

        var (_, previewBytes) = await client.ReadAll(new ReadFileRequest { FileId = response.Uploaded.Preview.Id });
        var previewInfo = Image.Identify(previewBytes);
        Assert.Equal(100, previewInfo.Width);
        Assert.Equal(50, previewInfo.Height);
    }

    [Fact]
    public async Task UploadFile_ShouldStoreWithoutPreview_WhenFileIsNotImage()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var text = TestFiles.Text("# Notes");

        var response = await client.UploadFileAsync(TestFiles.Upload(text, "notes.md", "text/markdown"));

        Assert.Null(response.Uploaded.Preview);
        Assert.Equal(text.Length, response.Uploaded.OwnerUsageBytes);
        Assert.Equal(1, await testScope.Database.StoredFiles.CountAsync());
        Assert.True(File.Exists(Path.Combine(host.BucketDirectory, "files", Guid.Parse(response.Uploaded.File.Id).ToString("N"))));
    }

    [Fact]
    public async Task UploadFile_ShouldReturnExistingFile_WhenSameContentIsUploadedForSameOwner()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var png = TestFiles.Png(800, 800);

        var first = await client.UploadFileAsync(TestFiles.Upload(png, "a.png", "image/png"));
        var second = await client.UploadFileAsync(TestFiles.Upload(png, "b.png", "image/png"));

        Assert.True(second.Uploaded.Deduplicated);
        Assert.Equal(first.Uploaded.File.Id, second.Uploaded.File.Id);
        Assert.Equal(first.Uploaded.Preview.Id, second.Uploaded.Preview.Id);
        Assert.Equal(first.Uploaded.OwnerUsageBytes, second.Uploaded.OwnerUsageBytes);

        var original = await testScope.Database.StoredFiles.SingleAsync(x => x.Kind == Domain.StoredFileKind.Original);
        Assert.Equal(2, original.References);
    }

    [Fact]
    public async Task UploadFile_ShouldStoreSeparately_WhenSameContentIsUploadedForAnotherOwner()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var text = TestFiles.Text("same");

        var first = await client.UploadFileAsync(TestFiles.Upload(text, owner: "organization:1"));
        var second = await client.UploadFileAsync(TestFiles.Upload(text, owner: "organization:2"));

        Assert.False(second.Uploaded.Deduplicated);
        Assert.NotEqual(first.Uploaded.File.Id, second.Uploaded.File.Id);
        Assert.Equal(text.Length, second.Uploaded.OwnerUsageBytes);
    }

    [Fact]
    public async Task UploadFile_ShouldReturnQuotaExceeded_WhenFileDoesNotFitInQuota()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var first = TestFiles.Upload(TestFiles.Text(new string('a', 60)));
        first.QuotaBytes = 100;
        await client.UploadFileAsync(first);

        var second = TestFiles.Upload(TestFiles.Text(new string('b', 50)), "more.txt");
        second.QuotaBytes = 100;
        var response = await client.UploadFileAsync(second);

        Assert.Equal(UploadFileResponse.ResultOneofCase.QuotaExceeded, response.ResultCase);
        Assert.Equal(60, response.QuotaExceeded.OwnerUsageBytes);
        Assert.Equal(100, response.QuotaExceeded.QuotaBytes);
        Assert.Equal(50, response.QuotaExceeded.RequiredBytes);
        Assert.Equal(1, await testScope.Database.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task UploadFile_ShouldAcceptDuplicate_WhenQuotaIsFull()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload(TestFiles.Text(new string('a', 100)));
        request.QuotaBytes = 100;

        await client.UploadFileAsync(request);
        var response = await client.UploadFileAsync(request);

        Assert.True(response.Uploaded.Deduplicated);
    }

    [Fact]
    public async Task UploadFile_ShouldReturnInvalidArgument_WhenFileIsEmpty()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.UploadFileAsync(TestFiles.Upload([])).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadFile_ShouldReturnInvalidArgument_WhenOwnerIsEmpty(string owner)
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("a"), owner: owner)).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadFile_ShouldReturnInvalidArgument_WhenFileIsLargerThanAccepted()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload([]);
        request.Content = ByteString.CopyFrom(new byte[20_000_001]);

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.UploadFileAsync(request).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task UploadFile_ShouldReturnInvalidArgument_WhenServiceIdHeaderIsMissing()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClientWithoutServiceIdHeader();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("a"))).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
