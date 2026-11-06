using Laraue.Apps.Storage.WebApiServices;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Storage.WebApiHost.Controllers;

/// <summary>
/// <c>GET /files/&lt;id&gt;</c> and <c>GET /previews/&lt;id&gt;.webp</c>: the file, streamed from the bucket with the
/// headers stored at upload (type, disposition, a day's cache). Anything else is 404. Byte ranges are served by
/// Cloudflare from its cached copy; this host always sends the whole file.
/// </summary>
[ApiController]
public sealed class FilesController(IPublicFilesService publicFilesService) : ControllerBase
{
    [HttpGet("{kind:regex(^(files|previews)$)}/{name}")]
    public async Task<IActionResult> GetFile(string kind, string name, CancellationToken cancellationToken)
    {
        var file = await publicFilesService.OpenAsync($"{kind}/{name}", cancellationToken);
        if (file is null)
            return NotFound();

        HttpContext.Response.RegisterForDisposeAsync(file);

        // Uploaded content: never sniffed into another type, never run as a page.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers.ContentDisposition = file.Headers.ContentDisposition;
        Response.Headers.CacheControl = file.Headers.CacheControl;
        Response.ContentLength = file.Length;

        return File(file.Content, file.Headers.ContentType);
    }
}
