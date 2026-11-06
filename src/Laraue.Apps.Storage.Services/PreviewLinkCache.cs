using Microsoft.Extensions.Caching.Memory;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Preview download links handed out today, so a preview keeps the same link all day (see
/// <c>FilesService.GetDownloadLocationAsync</c>). A cache of its own with a size limit, not the host's shared
/// <see cref="IMemoryCache"/>, whose other users don't set entry sizes.
/// </summary>
public sealed class PreviewLinkCache : IDisposable
{
    /// <summary>
    /// At most this many links (a few hundred bytes each) are kept.
    /// </summary>
    public const int MaxEntries = 50_000;

    public MemoryCache Cache { get; } = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    public void Dispose()
    {
        Cache.Dispose();
    }
}
