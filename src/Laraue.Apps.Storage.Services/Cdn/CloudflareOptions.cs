using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services.Cdn;

/// <summary>
/// Section <c>Cloudflare</c>: the zone the files domain is in, and an API token allowed only "Zone - Cache Purge"
/// on it.
/// </summary>
public class CloudflareOptions
{
    [Required]
    public required string ZoneId { get; set; }

    [Required]
    public required string ApiToken { get; set; }
}
