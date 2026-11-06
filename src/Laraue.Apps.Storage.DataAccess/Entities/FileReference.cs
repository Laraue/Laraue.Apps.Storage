namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// One use of an original by the calling service: every <c>UploadFile</c> adds one (a duplicate too), and its id is
/// what the caller keeps and later deletes. It starts <see cref="FileReferenceState.Pending"/>; the caller confirms it
/// once it has saved its own link to the file. Pending references older than
/// <c>Storage:PendingReferenceLifetime</c> are deleted by <c>PendingReferencesCleaner</c>, so an upload the caller
/// never saved doesn't take space forever. The file goes when its last reference does.
/// </summary>
public class FileReference
{
    /// <summary>
    /// A UUIDv7, returned by <c>UploadFile</c> as <c>reference_id</c>.
    /// </summary>
    public Guid Id { get; set; }

    public Guid FileId { get; set; }
    public StoredFile? File { get; set; }

    public FileReferenceState State { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }
}

public enum FileReferenceState
{
    Pending = 0,
    Confirmed = 1,
}
