using Laraue.Apps.Storage.DataAccess.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// The two places file bytes are kept, registered as keyed <see cref="IFileBucket"/>s: <see cref="OriginalsKey"/>
/// (the bucket) and <see cref="PreviewsKey"/> (the local disk).
/// </summary>
public sealed class FileBuckets(
    [FromKeyedServices(FileBuckets.OriginalsKey)] IFileBucket originals,
    [FromKeyedServices(FileBuckets.PreviewsKey)] IFileBucket previews)
{
    public const string OriginalsKey = "originals";
    public const string PreviewsKey = "previews";

    public IFileBucket For(StoredFileLocation location) => location switch
    {
        StoredFileLocation.Bucket => originals,
        StoredFileLocation.LocalDisk => previews,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };
}
