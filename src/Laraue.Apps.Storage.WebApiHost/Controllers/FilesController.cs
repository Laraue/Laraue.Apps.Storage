using Laraue.Apps.Storage.WebApiServices;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Storage.WebApiHost.Controllers;

/// <summary>
/// <c>GET /&lt;id&gt;</c>, an original or a preview by its id: the file, streamed from the bucket with the
/// headers stored at upload (type, disposition, a day's cache). Anything else is 404. Byte ranges are served by
/// Cloudflare from its cached copy; this host always sends the whole file.
/// </summary>
[ApiController]
public sealed class FilesController(IPublicFilesService publicFilesService) : ControllerBase
{
    [HttpGet("{id:regex(^[[0-9a-f]]{{32}}$)}")]
    public async Task<IActionResult> GetFile(string id, CancellationToken cancellationToken)
    {
        var file = await publicFilesService.OpenAsync(id, cancellationToken);
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
