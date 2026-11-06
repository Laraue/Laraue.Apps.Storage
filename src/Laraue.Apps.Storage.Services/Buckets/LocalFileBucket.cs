namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over a local directory, for tests and local runs without a bucket
/// (<c>Bucket:LocalDirectory</c>). Its download links are <c>file://</c> URLs: tests read them, a browser can't -
/// to click through downloads locally, point <c>Bucket:*</c> at a development B2 bucket or a local S3 server
/// (MinIO) instead.
/// </summary>
public class LocalFileBucket(string directory) : IFileBucket
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
        return Task.FromResult(new Uri(GetPath(key)).AbsoluteUri);
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
    private string GetPath(string key)
    {
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, key));

        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"Key {key} is outside the bucket directory.", nameof(key));
    }
}
