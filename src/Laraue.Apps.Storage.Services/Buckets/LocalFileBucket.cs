namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over a local directory, for tests (<c>Bucket:LocalDirectory</c>). Headers aren't kept.
/// With <c>Storage:PublicBaseUrl</c> set to the directory's <c>file://</c> URL, the files' URLs point into it.
/// </summary>
public class LocalFileBucket(string directory) : IFileBucket
{
    public async Task Write(string key, Stream content, FileObjectHeaders headers, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fileStream, cancellationToken);
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
