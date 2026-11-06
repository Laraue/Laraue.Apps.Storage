using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PublicProgram = Laraue.Apps.Storage.WebApiHost.Program;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

/// <summary>
/// The public web host over the same folders as an <see cref="InternalApiTestHost"/>, with the same signing key.
/// </summary>
public class WebApiTestHost(string previewsDirectory, string originalsDirectory) : WebApplicationFactory<PublicProgram>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:PreviewsDirectory"] = previewsDirectory,
            ["Bucket:LocalDirectory"] = originalsDirectory,
            ["FileLinks:SigningKey"] = InternalApiTestHost.SigningKey,
        }));

        return base.CreateHost(builder);
    }
}
