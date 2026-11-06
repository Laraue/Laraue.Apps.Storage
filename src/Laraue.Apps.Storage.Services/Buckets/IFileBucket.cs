namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// Where file bytes are kept. Originals: <see cref="S3FileBucket"/> in production, <see cref="LocalFileBucket"/>
/// for local runs and tests. Previews: always a <see cref="LocalFileBucket"/> on the service's disk.
/// </summary>
public interface IFileBucket
{
    /// <summary>
    /// Writes <paramref name="content"/> (seekable, read from its current position) under <paramref name="key"/>,
    /// replacing what was there.
    /// </summary>
    Task Write(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the object, or the part of it <paramref name="range"/> asks for. Throws
    /// <see cref="FileBucketRangeNotSatisfiableException"/> when the range starts past the end. The caller owns
    /// the returned stream.
    /// </summary>
    Task<FileBucketObject> Read(string key, FileBucketRange? range, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the object. Deleting a missing one is not an error.
    /// </summary>
    Task Delete(string key, CancellationToken cancellationToken);
}

/// <summary>
/// A single HTTP byte range: <c>bytes=From-To</c>, <c>bytes=From-</c> (<see cref="To"/> null) or the last
/// <see cref="SuffixLength"/> bytes (<c>bytes=-N</c>).
/// </summary>
public record FileBucketRange(long? From, long? To, long? SuffixLength = null);

/// <summary>
/// What <see cref="IFileBucket.Read"/> returned: <see cref="Content"/> holds bytes <see cref="From"/>..<see cref="To"/>
/// (inclusive) of an object of <see cref="TotalLength"/> bytes. <see cref="IsPartial"/> is whether a range was
/// applied, i.e. the response is a 206.
/// </summary>
public record FileBucketObject(Stream Content, long From, long To, long TotalLength, bool IsPartial)
{
    public long Length => TotalLength == 0 ? 0 : To - From + 1;
}

public class FileBucketRangeNotSatisfiableException(long totalLength)
    : Exception($"The requested range is outside the object's {totalLength} bytes.")
{
    public long TotalLength { get; } = totalLength;
}
