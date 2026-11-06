using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Links;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Laraue.Apps.Storage.WebApiServices;

/// <summary>
/// <c>GET /files/{area}/{key}</c>: a file from this server's disk, for a link the internal host signed
/// (<see cref="FileLinkSigner"/>). The link is the only permission: no database, no session. A missing file, a
/// wrong signature and an expired link are all 404, so the response says nothing about which files exist.
/// </summary>
public static class FilesEndpoint
{
    public static IEndpointRouteBuilder MapFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{FileLinkSigner.RoutePrefix}/{{area}}/{{**key}}", (
            string area,
            string key,
            HttpContext context,
            FileLinkSigner signer,
            IOptions<ServedFilesOptions> options,
            IDateTimeProvider dateTimeProvider) =>
        {
            var query = context.Request.Query;
            var contentType = query[FileLinkSigner.ContentTypeParameter].ToString();
            var disposition = query[FileLinkSigner.DispositionParameter].ToString();
            var signature = query[FileLinkSigner.SignatureParameter].ToString();
            var now = dateTimeProvider.UtcNow;

            if (!long.TryParse(query[FileLinkSigner.ExpiresParameter], out var expires)
                || !signer.IsValid(area, key, contentType, disposition, expires, signature, now))
            {
                return Results.NotFound();
            }

            var directory = area switch
            {
                LocalFileBucket.PreviewsArea => options.Value.PreviewsDirectory,
                LocalFileBucket.OriginalsArea => options.Value.OriginalsDirectory,
                _ => null,
            };

            if (string.IsNullOrEmpty(directory))
                return Results.NotFound();

            string path;
            try
            {
                path = new LocalFileBucket(directory, area).GetPath(key);
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }

            if (!System.IO.File.Exists(path))
                return Results.NotFound();

            var headers = context.Response.Headers;
            headers.ContentDisposition = disposition;
            // Uploaded content: never sniffed into another type, never run as a page.
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "sandbox; default-src 'none'";

            // The link's own lifetime: a preview's link is the same all day, so the browser keeps the file that long.
            var maxAge = Math.Max(0, expires - new DateTimeOffset(now).ToUnixTimeSeconds());
            headers.CacheControl = new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromSeconds(maxAge) }.ToString();

            return Results.File(path, contentType, enableRangeProcessing: true);
        });

        return app;
    }
}
