using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Previews;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.InternalApiServices;

public static class ServiceCollectionExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddInternalApiServices()
        {
            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddMetrics()
                .AddSingleton<StorageMetrics>()
                .AddSingleton<IPreviewMaker, PreviewMaker>()
                .AddScoped<IFilesService, FilesService>()
                .AddSingleton<StorageStateMetrics>()
                .AddHostedService(sp => sp.GetRequiredService<StorageStateMetrics>())
                .AddSingleton<PendingReferencesCleaner>()
                .AddHostedService(sp => sp.GetRequiredService<PendingReferencesCleaner>());

            builder.AddValidatedOptions<StorageOptions>("Storage");

            // Originals and previews go to the B2 bucket. Bucket:LocalDirectory (tests) keeps them in a folder
            // instead, with no credentials needed; it's never set in production.
            var localBucketDirectory = builder.Configuration["Bucket:LocalDirectory"];
            if (string.IsNullOrEmpty(localBucketDirectory))
            {
                builder.AddValidatedOptions<FileBucketOptions>("Bucket");
                builder.Services.AddSingleton<IFileBucket, S3FileBucket>();
            }
            else
            {
                builder.Services.AddSingleton<IFileBucket>(new LocalFileBucket(localBucketDirectory));
            }

            return builder;
        }

        /// <summary>
        /// Binds <typeparamref name="TOptions"/> to the given configuration section. In the Production
        /// environment it also validates the options' data annotations and <paramref name="validation"/>
        /// when the host starts, so a missing setting stops the host on deploy instead of failing on first
        /// use. Other environments only bind. Same helper as Boards' and Feedback's.
        /// </summary>
        public OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
            string sectionName,
            Func<TOptions, bool>? validation = null,
            string? failureMessage = null)
            where TOptions : class
        {
            var options = builder.Services
                .AddOptions<TOptions>()
                .Bind(builder.Configuration.GetSection(sectionName));

            if (!builder.Environment.IsProduction())
            {
                return options;
            }

            options.ValidateDataAnnotations();
            if (validation is not null)
            {
                options.Validate(validation, failureMessage ?? $"{sectionName} is invalid.");
            }

            return options.ValidateOnStart();
        }
    }
}
