using Laraue.Apps.Storage.IntegrationTests.Infrastructure;

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
}
