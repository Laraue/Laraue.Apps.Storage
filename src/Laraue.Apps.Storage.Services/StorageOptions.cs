using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Section <c>Storage</c>.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// The largest file accepted, whatever the caller's own limits. Callers check their own, smaller ones
    /// first (Boards: 3 MB images, 10 MB documents).
    /// </summary>
    [Range(1, 100_000_000)]
    public int MaxFileSizeBytes { get; set; } = 20_000_000;

    /// <summary>
    /// Where clients download files: the bucket's public URL on the files domain, behind Cloudflare, e.g.
    /// <c>https://files.laraue.com/file/laraue-files</c>. A file's URL is this plus its key
    /// (<c>/files/&lt;id&gt;</c>, <c>/previews/&lt;id&gt;.webp</c>).
    /// </summary>
    [Required]
    [Url]
    public required string PublicBaseUrl { get; set; }

    /// <summary>
    /// How long a reference may stay pending (uploaded, not confirmed by the caller) before it's deleted with its
    /// file. Long enough for a caller to retry confirming after an outage of this service.
    /// </summary>
    public TimeSpan PendingReferenceLifetime { get; set; } = TimeSpan.FromHours(24);
}
