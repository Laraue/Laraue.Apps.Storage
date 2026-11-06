namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over a local directory: previews, and originals in local runs and tests
/// (<c>Bucket:LocalDirectory</c>), so those need no bucket credentials. Download links point at
/// <paramref name="downloadBaseUrl"/> - the host's local-files endpoint, mapped only when
/// <c>Storage:LocalFilesBaseUrl</c> is set (local runs, tests).
/// </summary>
public class LocalFileBucket(string directory, string? downloadBaseUrl = null) : IFileBucket
{
    public async Task Write(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<string> GetDownloadUrl(string key, FileDownload download)
    {
        if (downloadBaseUrl is null)
            throw new InvalidOperationException("Local files have no download links without Storage:LocalFilesBaseUrl.");

        var query = $"contentType={Uri.EscapeDataString(download.ContentType)}"
            + $"&disposition={Uri.EscapeDataString(download.ContentDisposition)}";

        return Task.FromResult($"{downloadBaseUrl.TrimEnd('/')}/{key.Replace('\\', '/')}?{query}");
    }

    public Task Delete(string key, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The full path of <paramref name="key"/>, refusing anything that would land outside the directory.
    /// </summary>
    public string GetPath(string key)
    {
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, key));

        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"Key {key} is outside the bucket directory.", nameof(key));
    }
}
