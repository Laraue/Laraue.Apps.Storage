using Grpc.Core;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Laraue.Apps.Storage.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Domain = Laraue.Apps.Storage.DataAccess.Entities;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class ReferenceTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task UploadFile_ShouldAddPendingReference_WhenFileIsUploaded()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("hello")))).Uploaded;

        var reference = await testScope.Database.FileReferences.SingleAsync();
        Assert.Equal(Guid.Parse(uploaded.ReferenceId), reference.Id);
        Assert.Equal(Domain.FileReferenceState.Pending, reference.State);
    }

    [Fact]
    public async Task ConfirmReference_ShouldConfirm_WhenCalledTwice()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("hello")))).Uploaded;

        await client.ConfirmReferenceAsync(new ConfirmReferenceRequest { ReferenceId = uploaded.ReferenceId });
        await client.ConfirmReferenceAsync(new ConfirmReferenceRequest { ReferenceId = uploaded.ReferenceId });

        var reference = await testScope.Database.FileReferences.SingleAsync();
        Assert.Equal(Domain.FileReferenceState.Confirmed, reference.State);
        Assert.NotNull(reference.ConfirmedAt);
    }

    [Fact]
    public async Task ConfirmReference_ShouldReturnNotFound_WhenReferenceIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var exception = await Assert.ThrowsAsync<RpcException>(() => client.ConfirmReferenceAsync(
            new ConfirmReferenceRequest { ReferenceId = Guid.NewGuid().ToString() }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task DeleteReference_ShouldDeleteFileAndPreview_WhenLastReferenceIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;

        var response = await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = uploaded.ReferenceId });

        Assert.True(response.FileDeleted);
        Assert.Equal(0, response.OwnerUsageBytes);
        Assert.Equal(0, await testScope.Database.StoredFiles.CountAsync());
        Assert.Empty(Directory.EnumerateFiles(host.BucketDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(0, (await client.GetUsageAsync(new GetUsageRequest { Owner = TestFiles.Organization() })).UsageBytes);
    }

    [Fact]
    public async Task DeleteReference_ShouldKeepFile_WhenAnotherReferenceIsLeft()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload(TestFiles.Text("shared"));
        var first = (await client.UploadFileAsync(request)).Uploaded;
        var second = (await client.UploadFileAsync(request)).Uploaded;

        var response = await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = first.ReferenceId });

        Assert.False(response.FileDeleted);
        Assert.Equal(6, response.OwnerUsageBytes);
        Assert.Equal("shared", System.Text.Encoding.UTF8.GetString(await host.Download(client, first.File.Id)));

        var last = await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = second.ReferenceId });

        Assert.True(last.FileDeleted);
        Assert.Equal(0, last.OwnerUsageBytes);
    }

    [Fact]
    public async Task DeleteReference_ShouldDoNothing_WhenRetried()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload(TestFiles.Text("shared"));
        var first = (await client.UploadFileAsync(request)).Uploaded;
        await client.UploadFileAsync(request);

        await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = first.ReferenceId });
        var retry = await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = first.ReferenceId });

        Assert.False(retry.FileDeleted);
        Assert.Equal(1, await testScope.Database.FileReferences.CountAsync());
        Assert.Equal(1, await testScope.Database.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task DeleteReference_ShouldDoNothing_WhenReferenceIsUnknown()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();

        var response = await client.DeleteReferenceAsync(new DeleteReferenceRequest { ReferenceId = Guid.NewGuid().ToString() });

        Assert.False(response.FileDeleted);
    }

    [Fact]
    public async Task PendingReferencesCleaner_ShouldDeleteFile_WhenUploadWasNeverConfirmed()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;
        await AgeReferences(testScope, TimeSpan.FromHours(25));

        var deleted = await host.Services.GetRequiredService<PendingReferencesCleaner>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await testScope.Database.StoredFiles.CountAsync());
        Assert.Empty(Directory.EnumerateFiles(host.BucketDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(0, (await client.GetUsageAsync(new GetUsageRequest { Owner = TestFiles.Organization() })).UsageBytes);
        await Assert.ThrowsAsync<RpcException>(() => client.ConfirmReferenceAsync(
            new ConfirmReferenceRequest { ReferenceId = uploaded.ReferenceId }).ResponseAsync);
    }

    [Fact]
    public async Task PendingReferencesCleaner_ShouldKeepFile_WhenReferenceIsConfirmed()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("kept")))).Uploaded;
        await client.ConfirmReferenceAsync(new ConfirmReferenceRequest { ReferenceId = uploaded.ReferenceId });
        await AgeReferences(testScope, TimeSpan.FromDays(3));

        var deleted = await host.Services.GetRequiredService<PendingReferencesCleaner>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await testScope.Database.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task PendingReferencesCleaner_ShouldKeepFile_WhenPendingReferenceIsRecent()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("recent")));
        await AgeReferences(testScope, TimeSpan.FromHours(23));

        var deleted = await host.Services.GetRequiredService<PendingReferencesCleaner>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await testScope.Database.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task PendingReferencesCleaner_ShouldKeepFile_WhenAnotherReferenceIsConfirmed()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var request = TestFiles.Upload(TestFiles.Text("shared"));
        var kept = (await client.UploadFileAsync(request)).Uploaded;
        await client.ConfirmReferenceAsync(new ConfirmReferenceRequest { ReferenceId = kept.ReferenceId });
        await client.UploadFileAsync(request);
        await AgeReferences(testScope, TimeSpan.FromHours(25));

        var deleted = await host.Services.GetRequiredService<PendingReferencesCleaner>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(1, await testScope.Database.FileReferences.CountAsync());
        Assert.Equal(6, (await client.GetUsageAsync(new GetUsageRequest { Owner = TestFiles.Organization() })).UsageBytes);
    }

    private static Task AgeReferences(InternalApiTestHostScope testScope, TimeSpan age)
    {
        return testScope.Database.FileReferences.ExecuteUpdateAsync(
            x => x.SetProperty(r => r.CreatedAt, r => r.CreatedAt - age));
    }
}
