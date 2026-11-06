using Laraue.Apps.Storage.Services.Links;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Laraue.Apps.Storage.WebApiServices;

public static class ServiceCollectionExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddWebApiServices()
        {
            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton<FileLinkSigner>();

            AddValidatedOptions<FileLinkOptions>(builder, builder.Configuration.GetSection("FileLinks"));

            var served = builder.Services
                .AddOptions<ServedFilesOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    options.PreviewsDirectory = configuration["Storage:PreviewsDirectory"] ?? string.Empty;
                    options.OriginalsDirectory = configuration["Bucket:LocalDirectory"];
                });

            if (builder.Environment.IsProduction())
                served.ValidateDataAnnotations().ValidateOnStart();

            return builder;
        }
    }

    /// <summary>
    /// Binds the options; in Production also validates their data annotations at startup, like the internal host's
    /// <c>AddValidatedOptions</c>.
    /// </summary>
    private static void AddValidatedOptions<TOptions>(WebApplicationBuilder builder, IConfigurationSection section)
        where TOptions : class
    {
        var options = builder.Services
            .AddOptions<TOptions>()
            .Bind(section);

        if (builder.Environment.IsProduction())
            options.ValidateDataAnnotations().ValidateOnStart();
    }
}
