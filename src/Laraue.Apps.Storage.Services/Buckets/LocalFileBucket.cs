using Laraue.Apps.Storage.Services.Links;

namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over a local directory: previews, and originals in local runs and tests
/// (<c>Bucket:LocalDirectory</c>), so those need no bucket credentials. Download links are signed links to the
/// public web host, which serves <paramref name="area"/> from its own mount of the same directory. The web host
/// itself only reads paths (<see cref="GetPath"/>) and passes no <paramref name="links"/>.
/// </summary>
public class LocalFileBucket(string directory, string area, LocalFileLinks? links = null) : IFileBucket
{
    public const string PreviewsArea = "previews";
    public const string OriginalsArea = "originals";

    public string Area => area;

    public async Task Write(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<string> GetDownloadUrl(string key, FileDownload download)
    {
        if (links is null)
            throw new InvalidOperationException("This bucket only serves paths; it can't sign download links.");

        return Task.FromResult(links.Signer.CreateUrl(links.PublicBaseUrl, area, key, download));
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

/// <summary>
/// What a <see cref="LocalFileBucket"/> needs to hand out links: the signer and the public web host's address.
/// </summary>
public sealed record LocalFileLinks(FileLinkSigner Signer, string PublicBaseUrl);
