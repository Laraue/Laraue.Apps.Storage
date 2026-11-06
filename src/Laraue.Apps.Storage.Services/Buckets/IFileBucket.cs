namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// Where file bytes are kept, originals and previews alike: <see cref="S3FileBucket"/> (a private B2 bucket) in
/// production, <see cref="LocalFileBucket"/> in tests. The internal host writes and deletes; the public web host
/// reads (with a read-only key) and streams files to Cloudflare, which caches them.
/// </summary>
public interface IFileBucket
{
    /// <summary>
    /// Writes <paramref name="content"/> (seekable, read from its current position) under <paramref name="key"/>,
    /// replacing what was there. <paramref name="headers"/> are stored with the object and sent with every download.
    /// </summary>
    Task Write(string key, Stream content, FileObjectHeaders headers, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the object with the headers it was written with, or returns null when there's no such object. The
    /// caller disposes it.
    /// </summary>
    Task<FileObject?> Read(string key, CancellationToken cancellationToken);

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

/// <summary>
/// An object being read: its content (<see cref="Length"/> bytes) and the headers stored with it.
/// </summary>
public sealed record FileObject(Stream Content, long Length, FileObjectHeaders Headers) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
