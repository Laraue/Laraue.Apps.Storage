using Laraue.Apps.Storage.WebApiServices;
using OpenTelemetry.Metrics;

namespace Laraue.Apps.Storage.WebApiHost;

/// <summary>
/// The public side of the storage service: serves files from this server's disk (previews) for links the internal
/// host signed. No database, no gRPC, nothing else public. <c>/_health</c> and <c>/_metrics</c> are for the docker
/// network only - nginx proxies <c>/files/</c> alone.
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
