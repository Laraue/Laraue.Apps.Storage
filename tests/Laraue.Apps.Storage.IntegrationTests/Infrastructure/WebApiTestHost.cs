using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using PublicProgram = Laraue.Apps.Storage.WebApiHost.Program;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

/// <summary>
/// The public host over an <see cref="InternalApiTestHost"/>'s bucket folder.
/// </summary>
public class WebApiTestHost(string bucketDirectory) : WebApplicationFactory<PublicProgram>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bucket:LocalDirectory"] = bucketDirectory,
        }));

        builder.ConfigureServices(services => services.Configure<PrometheusAspNetCoreOptions>(
            options => options.ScrapeResponseCacheDurationMilliseconds = 0));

        return base.CreateHost(builder);
    }
}
