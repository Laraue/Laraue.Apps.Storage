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
    /// How long a signed download link to an original works. Callers redirect to it at once, so it only needs to
    /// outlive the redirect and the client's range requests on the same link (a video being played). Preview links
    /// work until the end of the next day instead, see <c>FilesService</c>.
    /// </summary>
    public TimeSpan DownloadUrlLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a reference may stay pending (uploaded, not confirmed by the caller) before it's deleted with its
    /// file. Long enough for a caller to retry confirming after an outage of this service.
    /// </summary>
    public TimeSpan PendingReferenceLifetime { get; set; } = TimeSpan.FromHours(24);
}
