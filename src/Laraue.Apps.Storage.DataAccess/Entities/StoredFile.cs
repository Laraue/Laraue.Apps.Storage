using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// One stored file: an uploaded original in the bucket, or the preview made from an image on the local disk.
/// </summary>
public class StoredFile
{
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 128;

    /// <summary>
    /// A UUIDv7, what callers keep and pass back.
    /// </summary>
    public Guid Id { get; set; }

    public long OwnerId { get; set; }
    public Owner? Owner { get; set; }

    public StoredFileKind Kind { get; set; }

    public StoredFileLocation Location { get; set; }

    /// <summary>
    /// The object key in the bucket, or the path under <c>StorageOptions.PreviewsDirectory</c>.
    /// </summary>
    [MaxLength(255)]
    public required string StorageKey { get; set; }

    [MaxLength(FileNameMaxLength)]
    public required string FileName { get; set; }

    [MaxLength(ContentTypeMaxLength)]
    public required string ContentType { get; set; }

    /// <summary>
    /// Stored size in bytes, what counts toward <see cref="Owner.UsageBytes"/>.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of an original's content, to find the same upload for the same owner. Null for
    /// previews.
    /// </summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    /// <summary>
    /// For a preview, the original it was made from. Deleted with it.
    /// </summary>
    public Guid? SourceFileId { get; set; }
    public StoredFile? SourceFile { get; set; }
    public StoredFile? Preview { get; set; }

    /// <summary>
    /// How many uploads returned this original and haven't been deleted. The file is deleted when it drops to
    /// zero. Always 0 for previews, which go with their original.
    /// </summary>
    public int References { get; set; }

    /// <summary>
    /// The global Laraue user id of whoever uploaded it first, when the caller said.
    /// </summary>
    public Guid? UploadedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum StoredFileKind
{
    Original = 0,
    Preview = 1,
}

public enum StoredFileLocation
{
    /// <summary>
    /// The file bucket (Backblaze B2, S3 API).
    /// </summary>
    Bucket = 0,

    /// <summary>
    /// The local disk under <c>StorageOptions.PreviewsDirectory</c>.
    /// </summary>
    LocalDisk = 1,
}
