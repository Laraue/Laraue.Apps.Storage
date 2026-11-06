using System.Text.Json;

namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over a local directory, for tests and local runs without a bucket
/// (<c>Bucket:LocalDirectory</c>). An object's headers are kept next to it in <c>&lt;key&gt;.headers.json</c>, as a
/// bucket keeps them with the object.
/// </summary>
public class LocalFileBucket(string directory) : IFileBucket
{
    private const string HeadersSuffix = ".headers.json";

    public async Task Write(string key, Stream content, FileObjectHeaders headers, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await content.CopyToAsync(fileStream, cancellationToken);

        await System.IO.File.WriteAllTextAsync(path + HeadersSuffix, JsonSerializer.Serialize(headers), cancellationToken);
    }

    public async Task<FileObject?> Read(string key, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        if (!System.IO.File.Exists(path) || !System.IO.File.Exists(path + HeadersSuffix))
            return null;

        var headers = JsonSerializer.Deserialize<FileObjectHeaders>(
            await System.IO.File.ReadAllTextAsync(path + HeadersSuffix, cancellationToken))!;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);

        return new FileObject(stream, stream.Length, headers);
    }

    public Task Delete(string key, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        System.IO.File.Delete(path);
        System.IO.File.Delete(path + HeadersSuffix);

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
