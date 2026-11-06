using System.Collections.Concurrent;
using Laraue.Apps.Storage.Services.Buckets;

namespace Laraue.Apps.Storage.IntegrationTests.Infrastructure;

/// <summary>
/// The test bucket folder, remembering the headers each object was written with (a local folder can't keep them).
/// </summary>
public sealed class RecordingFileBucket(string directory) : IFileBucket
{
    private readonly LocalFileBucket _inner = new(directory);

    public ConcurrentDictionary<string, FileObjectHeaders> Headers { get; } = new();

    public Task Write(string key, Stream content, FileObjectHeaders headers, CancellationToken cancellationToken)
    {
        Headers[key] = headers;
        return _inner.Write(key, content, headers, cancellationToken);
    }

    public Task<FileObject?> Read(string key, CancellationToken cancellationToken) => _inner.Read(key, cancellationToken);

    public Task Delete(string key, CancellationToken cancellationToken) => _inner.Delete(key, cancellationToken);
}
