using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;

namespace Laraue.Apps.Storage.WebApiServices;

public interface IPublicFilesService
{
    /// <summary>
    /// Opens the file with this id, or returns null when there's none. The caller disposes it.
    /// </summary>
    Task<FileObject?> OpenAsync(Guid fileId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads files for the public host from the private bucket, by id only - so nothing in the bucket but files is
/// reachable. Knowing a file's id is the access check: a UUIDv7, learned only from data a user could read - the same
/// model as an app's own link by file id.
/// </summary>
public sealed class PublicFilesService(IFileBucket bucket, StorageMetrics metrics) : IPublicFilesService
{
    public async Task<FileObject?> OpenAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await bucket.Read(FilesService.GetStorageKey(fileId), cancellationToken);
        metrics.RecordPublicRead(file is null
            ? StorageMetrics.PublicReadResults.NotFound
            : StorageMetrics.PublicReadResults.Served);

        return file;
    }
}
