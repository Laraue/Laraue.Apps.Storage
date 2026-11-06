using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Section <c>Storage</c>.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// Where previews are written: a volume on the server's disk. Previews are small and read on every board
    /// render, so they stay local instead of costing bucket downloads.
    /// </summary>
    [Required]
    public required string PreviewsDirectory { get; set; }

    /// <summary>
    /// The largest file accepted, whatever the caller's own limits. Callers check their own, smaller ones
    /// first (Boards: 3 MB images, 10 MB documents).
    /// </summary>
    [Range(1, 100_000_000)]
    public int MaxFileSizeBytes { get; set; } = 20_000_000;

    /// <summary>
    /// How long a signed download link to the bucket works. Callers redirect to it at once, so it only needs to
    /// outlive the redirect and the client's range requests on the same link (a video being played).
    /// </summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a reference may stay pending (uploaded, not confirmed by the caller) before it's deleted with its
    /// file. Long enough for a caller to retry confirming after an outage of this service.
    /// </summary>
    public TimeSpan PendingReferenceLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Where the public web host (<c>Laraue.Apps.Storage.WebApiHost</c>) is reached by clients, e.g.
    /// <c>https://files.laraue.com</c>. Links to files on this server's disk (previews, and originals with
    /// <c>Bucket:LocalDirectory</c>) point there. A domain of its own, so an uploaded file never runs as a page on
    /// an app's origin.
    /// </summary>
    [Required]
    [Url]
    public required string PublicBaseUrl { get; set; }
}
