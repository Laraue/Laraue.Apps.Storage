using System.Text.RegularExpressions;
using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;

namespace Laraue.Apps.Storage.WebApiServices;

public interface IPublicFilesService
{
    /// <summary>
    /// Opens the object at <paramref name="key"/> when it's a file or preview key (<c>files/&lt;id&gt;</c>,
    /// <c>previews/&lt;id&gt;.webp</c>) and exists; null otherwise, so nothing else in the bucket is reachable.
    /// The caller disposes it.
    /// </summary>
    Task<FileObject?> OpenAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Reads files for the public host from the private bucket. Knowing a file's key is the access check: it holds the
/// file's UUIDv7, learned only from data a user could read - the same model as an app's own link by file id.
/// </summary>
public sealed partial class PublicFilesService(IFileBucket bucket, StorageMetrics metrics) : IPublicFilesService
{
    public async Task<FileObject?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        if (!FileKey().IsMatch(key))
        {
            metrics.RecordPublicRead(StorageMetrics.PublicReadResults.NotFound);
            return null;
        }

        var file = await bucket.Read(key, cancellationToken);
        metrics.RecordPublicRead(file is null
            ? StorageMetrics.PublicReadResults.NotFound
            : StorageMetrics.PublicReadResults.Served);

        return file;
    }

    [GeneratedRegex("^(files/[0-9a-f]{32}|previews/[0-9a-f]{32}\\.webp)$")]
    private static partial Regex FileKey();
}
