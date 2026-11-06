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
    /// Local runs and tests only: this host's own address for local files, e.g.
    /// <c>http://localhost:5564/_local-files</c>. When set, files kept on this machine (previews, and originals with
    /// <c>Bucket:LocalDirectory</c>) get download links served by the host itself, since there's no nginx in front.
    /// Never set in production, where previews are served by nginx from the volume.
    /// </summary>
    public string? LocalFilesBaseUrl { get; set; }
}
