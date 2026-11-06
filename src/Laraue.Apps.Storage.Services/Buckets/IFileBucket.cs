namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// Where file bytes are kept, originals and previews alike: <see cref="S3FileBucket"/> (B2) in production,
/// <see cref="LocalFileBucket"/> in tests. Bytes are only written and deleted here; clients download them from the
/// public files domain (<c>Storage:PublicBaseUrl</c>), never through this service.
/// </summary>
public interface IFileBucket
{
    /// <summary>
    /// Writes <paramref name="content"/> (seekable, read from its current position) under <paramref name="key"/>,
    /// replacing what was there. <paramref name="headers"/> are stored with the object and sent with every download.
    /// </summary>
    Task Write(string key, Stream content, FileObjectHeaders headers, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the object. Deleting a missing one is not an error.
    /// </summary>
    Task Delete(string key, CancellationToken cancellationToken);
}

/// <summary>
/// What the bucket answers a download with. <see cref="ContentDisposition"/> is a full header value
/// (<c>inline</c> or <c>attachment; filename=...</c>).
/// </summary>
public sealed record FileObjectHeaders(string ContentType, string ContentDisposition, string CacheControl);
