using Laraue.Apps.Storage.IntegrationTests.Infrastructure;
using Laraue.Apps.Storage.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Storage.IntegrationTests;

[Collection("IntegrationTest")]
public class MetricsTests(InternalApiTestHost host) : IClassFixture<InternalApiTestHost>
{
    [Fact]
    public async Task Metrics_ShouldExportStorageCounters_WhenScraped()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        await client.UploadFileAsync(TestFiles.Upload(TestFiles.Text("metrics")));

        var body = await host.CreateClient().GetStringAsync("/_metrics");

        Assert.Contains("storage_uploads_total{", body);
        Assert.Contains("storage_bucket_failures_total{", body);
    }

    [Fact]
    public async Task Metrics_ShouldExportStoredTotals_WhenFilesAreStored()
    {
        using var testScope = host.CreateTestScope();
        var client = host.CreateStorageClient();
        var text = TestFiles.Text("twelve bytes");
        var uploaded = (await client.UploadFileAsync(TestFiles.Upload(TestFiles.Png(800, 600), "a.png", "image/png"))).Uploaded;
        await client.UploadFileAsync(TestFiles.Upload(text, owner: new Laraue.Apps.Storage.Internal.Contracts.Owner { UserId = Guid.NewGuid().ToString() }));
        await host.Services.GetRequiredService<StorageStateMetrics>().RefreshAsync(CancellationToken.None);

        var body = await host.CreateClient().GetStringAsync("/_metrics");

        Assert.Contains(Series("storage_stored_bytes", "kind=\"original\"", uploaded.File.Size + text.Length), body);
        Assert.Contains(Series("storage_stored_bytes", "kind=\"preview\"", uploaded.Preview.Size), body);
        Assert.Contains(Series("storage_stored_files", "kind=\"original\"", 2), body);
        Assert.Contains(Series("storage_owners", "kind=\"organization\"", 1), body);
        Assert.Contains(Series("storage_owners", "kind=\"user\"", 1), body);
        Assert.Contains(Series("storage_usage_bytes", null, uploaded.File.Size + uploaded.Preview.Size + text.Length), body);
    }

    private static string Series(string name, string? label, long value)
    {
        var labels = label is null ? string.Empty : $",{label}";
        return $"{name}{{otel_scope_name=\"Laraue.Apps.Storage\"{labels}}} {value}";
    }
}
