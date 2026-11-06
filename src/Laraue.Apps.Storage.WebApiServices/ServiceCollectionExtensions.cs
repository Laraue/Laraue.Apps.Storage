using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;
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
                .AddMetrics()
                .AddSingleton<StorageMetrics>()
                .AddScoped<IPublicFilesService, PublicFilesService>();

            // The same bucket as the internal host, with a key of its own that can only read. Bucket:LocalDirectory
            // (local runs, tests) reads the internal host's local folder instead.
            var localBucketDirectory = builder.Configuration["Bucket:LocalDirectory"];
            if (string.IsNullOrEmpty(localBucketDirectory))
            {
                var options = builder.Services
                    .AddOptions<FileBucketOptions>()
                    .Bind(builder.Configuration.GetSection("Bucket"));
                if (builder.Environment.IsProduction())
                    options.ValidateDataAnnotations().ValidateOnStart();

                builder.Services.AddSingleton<IFileBucket, S3FileBucket>();
            }
            else
            {
                builder.Services.AddSingleton<IFileBucket>(new LocalFileBucket(localBucketDirectory));
            }

            return builder;
        }
    }
}
