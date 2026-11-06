namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// The two places file bytes are kept: <see cref="Originals"/> (the bucket) and <see cref="Previews"/> (the
/// local disk).
/// </summary>
public sealed record FileBuckets(IFileBucket Originals, IFileBucket Previews)
{
    public IFileBucket For(DataAccess.Entities.StoredFileLocation location) => location switch
    {
        DataAccess.Entities.StoredFileLocation.Bucket => Originals,
        DataAccess.Entities.StoredFileLocation.LocalDisk => Previews,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };
}
