using System.Collections.Concurrent;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Cdn;

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

    public Task Delete(string key, CancellationToken cancellationToken) => _inner.Delete(key, cancellationToken);
}

/// <summary>
/// Stands in for Cloudflare: remembers the URLs it was asked to purge.
/// </summary>
public sealed class RecordingCdnCache : ICdnCache
{
    public ConcurrentQueue<string> PurgedUrls { get; } = new();

    public Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
            PurgedUrls.Enqueue(url);

        return Task.CompletedTask;
    }
}
