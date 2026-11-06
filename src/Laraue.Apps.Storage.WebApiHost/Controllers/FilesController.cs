using Laraue.Apps.Storage.Services.Links;
using Laraue.Apps.Storage.WebApiServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Laraue.Apps.Storage.WebApiHost.Controllers;

/// <summary>
/// Files from this server's disk (previews) for links the internal host signed. See <see cref="IServedFilesService"/>.
/// </summary>
[ApiController]
[Route(FileLinkSigner.RouteSegment)]
public sealed class FilesController(IServedFilesService servedFilesService) : ControllerBase
{
    [HttpGet("{area}/{**key}")]
    public IActionResult GetFile(
        string area,
        string key,
        [FromQuery(Name = FileLinkSigner.ContentTypeParameter)] string? contentType,
        [FromQuery(Name = FileLinkSigner.DispositionParameter)] string? contentDisposition,
        [FromQuery(Name = FileLinkSigner.ExpiresParameter)] long? expires,
        [FromQuery(Name = FileLinkSigner.SignatureParameter)] string? signature)
    {
        var file = servedFilesService.Resolve(new FileLink(area, key, contentType, contentDisposition, expires, signature));
        if (file is null)
            return NotFound();

        // Uploaded content: never sniffed into another type, never run as a page.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.ContentDisposition = file.ContentDisposition;
        Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            MaxAge = file.CacheFor > TimeSpan.Zero ? file.CacheFor : TimeSpan.Zero,
        }.ToString();

        return PhysicalFile(file.Path, file.ContentType, enableRangeProcessing: true);
    }
}
