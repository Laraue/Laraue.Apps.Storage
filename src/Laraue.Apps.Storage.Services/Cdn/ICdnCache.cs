namespace Laraue.Apps.Storage.Services.Cdn;

/// <summary>
/// The CDN in front of the public files domain. Objects never change (the key is the file id), so they're cached
/// for a year; a deleted file must be purged or it stays downloadable from the edge.
/// </summary>
public interface ICdnCache
{
    /// <summary>
    /// Drops these URLs from the CDN's cache. Throws when the CDN refused; the caller logs it.
    /// </summary>
    Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken);
}

/// <summary>
/// No CDN (tests, local runs): nothing to purge.
/// </summary>
public sealed class NoCdnCache : ICdnCache
{
    public Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken) => Task.CompletedTask;
}
