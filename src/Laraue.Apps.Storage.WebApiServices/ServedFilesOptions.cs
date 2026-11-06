using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.WebApiServices;

/// <summary>
/// The folders the public web host serves, read from the same settings as the internal host's:
/// <c>Storage:PreviewsDirectory</c> (its own read-only mount of the previews volume) and, in local runs and tests,
/// <c>Bucket:LocalDirectory</c>.
/// </summary>
public class ServedFilesOptions
{
    [Required]
    public required string PreviewsDirectory { get; set; }

    /// <summary>
    /// Originals kept locally instead of in the bucket (local runs, tests). Not set in production, where originals
    /// are downloaded from the bucket by signed links.
    /// </summary>
    public string? OriginalsDirectory { get; set; }
}
