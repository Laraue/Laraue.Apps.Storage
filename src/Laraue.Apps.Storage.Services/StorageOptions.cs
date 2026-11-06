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
}
