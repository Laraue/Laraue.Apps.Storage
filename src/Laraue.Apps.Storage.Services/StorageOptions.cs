using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Section <c>Storage</c>. The values are in <c>InternalApiHost</c>'s <c>appsettings.json</c>; production overrides
/// only <see cref="PublicBaseUrl"/>.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// The largest file accepted, whatever the caller's own limits. Callers check their own, smaller ones
    /// first (Boards: 3 MB images, 10 MB documents). 20 MB in <c>appsettings.json</c>; the gRPC host accepts
    /// messages up to 32 MB, so keep it below that.
    /// </summary>
    [Range(1, 30_000_000)]
    public int MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Where clients download files: the public host (<c>WebApiHost</c>) behind Cloudflare,
    /// <c>https://storage.laraue.com</c>. A file's URL is this plus its key
    /// (the file's id, <c>https://storage.laraue.com/&lt;id&gt;</c>).
    /// </summary>
    [Required]
    [Url]
    public required string PublicBaseUrl { get; set; }

    /// <summary>
    /// How long a reference may stay pending (uploaded, not confirmed by the caller) before it's deleted with its
    /// file. Long enough for a caller to retry confirming after an outage of this service: 24 hours in
    /// <c>appsettings.json</c>.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan PendingReferenceLifetime { get; set; }
}
