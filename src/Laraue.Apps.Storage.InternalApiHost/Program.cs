using Laraue.Apps.Storage.DataAccess;
using Laraue.Apps.Storage.InternalApiServices;
using Laraue.Apps.Storage.Services;
using Laraue.Grpc.OpenTelemetry;
using Laraue.Grpc.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;

namespace Laraue.Apps.Storage.InternalApiHost;

public sealed class Program
{
    /// <summary>
    /// The largest request accepted: an upload is one message holding the whole file
    /// (<see cref="StorageOptions.MaxFileSizeBytes"/>, 20 MB by default) plus its fields.
    /// </summary>
    private const int MaxReceiveMessageSize = 32 * 1024 * 1024;

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders().AddJsonConsole();

        const string dbConnectionStringName = "Postgre";

        // Two endpoints, not one Http1AndHttp2: Kestrel can't serve HTTP/1.1 and cleartext HTTP/2 on the
        // same endpoint without TLS - see "Project layout" in AGENTS.md.
        var grpcPort = builder.Configuration.GetValue<int?>("Kestrel:GrpcPort") ?? 5563;
        var healthPort = builder.Configuration.GetValue<int?>("Kestrel:HealthPort") ?? 5564;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
            options.ListenAnyIP(healthPort, listen => listen.Protocols = HttpProtocols.Http1);
        });

        var connection = builder.Configuration.GetConnectionString(dbConnectionStringName);
        builder.Services.AddDbContext<DatabaseContext>(opt => opt
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention());

        builder.AddInternalApiServices();

        builder.Services
            .AddGrpc(options => options.MaxReceiveMessageSize = MaxReceiveMessageSize)
            .AddLaraueGrpcTelemetry();

        builder.Services.AddLaraueGrpcTelemetry(
            configureMetrics: metrics => metrics
                .AddMeter(StorageMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapGrpcService<StorageGrpcService>();
        app.MapHealthChecks("/_health");
        app.MapPrometheusScrapingEndpoint("/_metrics");

        await app.RunAsync();
    }
}
