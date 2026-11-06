using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Laraue.Apps.Storage.DataAccess;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Apps.Storage.InternalApiHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Cdn;
using OpenTelemetry.Exporter;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

public class InternalApiTestHost : WebApplicationFactory<Program>
{
    /// <summary>
    /// Stands in for the B2 bucket, so no test reaches B2. Files' URLs are <c>file://</c> URLs into it.
    /// </summary>
    public string BucketDirectory { get; } = Path.Combine(Path.GetTempPath(), "storage-tests", Guid.NewGuid().ToString("N"), "bucket");

    public RecordingFileBucket Bucket { get; }

    public RecordingCdnCache Cdn { get; } = new();

    public InternalApiTestHost()
    {
        Bucket = new RecordingFileBucket(BucketDirectory);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bucket:LocalDirectory"] = BucketDirectory,
                ["Storage:PublicBaseUrl"] = new Uri(BucketDirectory).AbsoluteUri,
            });
        });

        // The exporter serves a cached scrape for 300 ms by default; a test scraping right after another
        // would read stale numbers.
        builder.ConfigureServices(services => services.Configure<PrometheusAspNetCoreOptions>(
            options => options.ScrapeResponseCacheDurationMilliseconds = 0));

        // Registered after the host's own, so these win.
        builder.ConfigureServices(services => services
            .AddSingleton<IFileBucket>(Bucket)
            .AddSingleton<ICdnCache>(Cdn));

        return base.CreateHost(builder);
    }

    /// <summary>
    /// A gRPC client wired to this in-memory test server, calling the way another Laraue app would - through
    /// <see cref="ServiceIdInterceptor"/>.
    /// </summary>
    public StorageService.StorageServiceClient CreateStorageClient(ServiceId callingService = ServiceId.LaraueBoards)
    {
        return new StorageService.StorageServiceClient(CreateChannel().Intercept(new ServiceIdInterceptor(callingService)));
    }

    public StorageService.StorageServiceClient CreateStorageClientWithoutServiceIdHeader()
    {
        return new StorageService.StorageServiceClient(CreateChannel());
    }

    private GrpcChannel CreateChannel()
    {
        var client = CreateDefaultClient();

        return GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = client,
            MaxReceiveMessageSize = null,
        });
    }

    /// <summary>
    /// Wipes the database and the bucket folder, so a test sees only its own files.
    /// </summary>
    public InternalApiTestHostScope CreateTestScope()
    {
        if (Directory.Exists(BucketDirectory))
            Directory.Delete(BucketDirectory, recursive: true);

        Bucket.Headers.Clear();
        Cdn.PurgedUrls.Clear();

        return new InternalApiTestHostScope(Services.CreateScope());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        var root = Directory.GetParent(BucketDirectory)!.FullName;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

/// <summary>
/// Per-test scope, same shape as Feedback's: creating one wipes the test database, so every test starts with
/// <c>using var testScope = host.CreateTestScope();</c> and uses <see cref="Database"/> for any direct assertions.
/// </summary>
public class InternalApiTestHostScope : IDisposable
{
    private readonly IServiceScope _scope;
    public DatabaseContext Database => _scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    public InternalApiTestHostScope(IServiceScope scope)
    {
        _scope = scope;
        Database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        Database.FileReferences.ExecuteDelete();
        Database.StoredFiles.Where(x => x.SourceFileId != null).ExecuteDelete();
        Database.StoredFiles.ExecuteDelete();
        Database.Owners.ExecuteDelete();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}
