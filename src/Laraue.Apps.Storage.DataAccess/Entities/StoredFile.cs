using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// One stored file in the bucket: an uploaded original (<c>files/&lt;id&gt;</c>), or the preview made from an image
/// (<c>previews/&lt;id&gt;.webp</c>).
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

    /// <summary>
    /// The object key in the bucket.
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
    /// The uses of an original (<see cref="FileReference"/>). The file is deleted with the last one. Previews have
    /// none; they go with their original.
    /// </summary>
    public ICollection<FileReference> References { get; set; } = [];

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
