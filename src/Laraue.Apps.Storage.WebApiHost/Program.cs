using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.WebApiServices;
using OpenTelemetry.Metrics;

namespace Laraue.Apps.Storage.WebApiHost;

/// <summary>
/// The public side of the storage service, <c>storage.laraue.com</c> behind Cloudflare: streams files from the
/// private bucket, and Cloudflare caches them, so only cache misses reach this host and B2. No database, no gRPC, a
/// read-only bucket key. <c>/_health</c> and <c>/_metrics</c> are for the docker network; nginx doesn't proxy them.
/// </summary>
public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddJsonConsole();

        builder.AddWebApiServices();
        builder.Services.AddControllers();

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(StorageMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.MapControllers();
        app.MapHealthChecks("/_health");
        app.MapPrometheusScrapingEndpoint("/_metrics");

        await app.RunAsync();
    }
}
