using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.InternalApiServices;

/// <summary>
/// Serves files kept on this machine over plain HTTP for local runs and tests, standing in for signed bucket
/// links and nginx. Mapped only when <see cref="StorageOptions.LocalFilesBaseUrl"/> is set, which it never is in
/// production; the base URL must end with <see cref="RoutePrefix"/>.
/// </summary>
public static class LocalFilesEndpoint
{
    public const string RoutePrefix = "/_local-files";
    public const string OriginalsArea = "originals";
    public const string PreviewsArea = "previews";

    public static void MapLocalFiles(this WebApplication app)
    {
        if (string.IsNullOrEmpty(app.Services.GetRequiredService<IOptions<StorageOptions>>().Value.LocalFilesBaseUrl))
            return;

        app.MapGet($"{RoutePrefix}/{{area}}/{{**key}}", (
            string area,
            string key,
            string? contentType,
            string? disposition,
            FileBuckets buckets,
            HttpContext context) =>
        {
            var bucket = area switch
            {
                OriginalsArea => buckets.Originals as LocalFileBucket,
                PreviewsArea => buckets.Previews as LocalFileBucket,
                _ => null,
            };

            if (bucket is null)
                return Results.NotFound();

            string path;
            try
            {
                path = bucket.GetPath(key);
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }

            if (!System.IO.File.Exists(path))
                return Results.NotFound();

            if (!string.IsNullOrEmpty(disposition))
                context.Response.Headers.ContentDisposition = disposition;

            return Results.File(path, contentType ?? "application/octet-stream", enableRangeProcessing: true);
        });
    }
}
