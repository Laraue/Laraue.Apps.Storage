using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// The bucket originals are kept in (section <c>Bucket</c>): a Backblaze B2 bucket of its own, separate from the
/// restic backup bucket, reached through the S3-compatible API with an application key limited to this bucket. The
/// key needs to list and delete files too: <see cref="S3FileBucket.Delete"/> removes every version.
/// </summary>
public class FileBucketOptions
{
    /// <summary>
    /// The S3 endpoint, e.g. <c>https://s3.eu-central-003.backblazeb2.com</c>.
    /// </summary>
    [Required]
    [Url]
    public required string ServiceUrl { get; set; }

    /// <summary>
    /// The region in <see cref="ServiceUrl"/>, e.g. <c>eu-central-003</c>; requests are signed for it.
    /// </summary>
    [Required]
    public required string Region { get; set; }

    [Required]
    public required string BucketName { get; set; }

    /// <summary>
    /// The application key id (the S3 access key).
    /// </summary>
    [Required]
    public required string KeyId { get; set; }

    /// <summary>
    /// The application key (the S3 secret key).
    /// </summary>
    [Required]
    public required string ApplicationKey { get; set; }
}
