namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// Where file bytes are kept, originals and previews alike: <see cref="S3FileBucket"/> (B2) in production,
/// <see cref="LocalFileBucket"/> in tests. Bytes are only written and deleted here - clients download them straight
/// from the bucket (<see cref="GetDownloadUrl"/>).
/// </summary>
public interface IFileBucket
{
    /// <summary>
    /// Writes <paramref name="content"/> (seekable, read from its current position) under <paramref name="key"/>,
    /// replacing what was there.
    /// </summary>
    Task Write(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// A link a client can download the object from until <see cref="FileDownload.ExpiresAt"/>, answered with the
    /// given content type and disposition. Doesn't check that the object exists.
    /// </summary>
    Task<string> GetDownloadUrl(string key, FileDownload download);

    /// <summary>
    /// Deletes the object. Deleting a missing one is not an error.
    /// </summary>
    Task Delete(string key, CancellationToken cancellationToken);
}

/// <summary>
/// How a download link answers: <see cref="ContentDisposition"/> is a full header value
/// (<c>inline</c> or <c>attachment; filename=...</c>).
/// </summary>
public sealed record FileDownload(string ContentType, string ContentDisposition, DateTime ExpiresAt);
