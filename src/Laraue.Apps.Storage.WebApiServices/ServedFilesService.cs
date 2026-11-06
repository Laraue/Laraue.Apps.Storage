using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Links;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.WebApiServices;

/// <summary>
/// A signed link's parameters, as <c>FileLinkSigner.CreateUrl</c> wrote them.
/// </summary>
public sealed record FileLink(
    string Area,
    string Key,
    string? ContentType,
    string? ContentDisposition,
    long? Expires,
    string? Signature);

/// <summary>
/// The file a valid link points at: its path on disk and how to answer with it, cacheable for
/// <see cref="CacheFor"/> (the link's remaining lifetime).
/// </summary>
public sealed record ServedFile(string Path, string ContentType, string ContentDisposition, TimeSpan CacheFor);

public interface IServedFilesService
{
    /// <summary>
    /// The file a link points at, or null for a wrong or expired signature, an unknown area, a path outside the
    /// area's folder or a missing file - the caller answers all of them the same way (404), so a response says
    /// nothing about which files exist.
    /// </summary>
    ServedFile? Resolve(FileLink link);
}

/// <summary>
/// Checks links the internal host signed (<see cref="FileLinkSigner"/>) and finds their files on this server's disk.
/// The link is the only permission: no database, no session.
/// </summary>
public sealed class ServedFilesService(
    FileLinkSigner signer,
    IOptions<ServedFilesOptions> options,
    IDateTimeProvider dateTimeProvider)
    : IServedFilesService
{
    public ServedFile? Resolve(FileLink link)
    {
        var now = dateTimeProvider.UtcNow;

        if (link.Expires is not { } expires
            || link.ContentType is null
            || link.ContentDisposition is null
            || link.Signature is null
            || !signer.IsValid(link.Area, link.Key, link.ContentType, link.ContentDisposition, expires, link.Signature, now))
        {
            return null;
        }

        var directory = link.Area switch
        {
            LocalFileBucket.PreviewsArea => options.Value.PreviewsDirectory,
            LocalFileBucket.OriginalsArea => options.Value.OriginalsDirectory,
            _ => null,
        };

        if (string.IsNullOrEmpty(directory))
            return null;

        string path;
        try
        {
            path = new LocalFileBucket(directory, link.Area).GetPath(link.Key);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!File.Exists(path))
            return null;

        var cacheFor = DateTimeOffset.FromUnixTimeSeconds(expires) - new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));

        return new ServedFile(path, link.ContentType, link.ContentDisposition, cacheFor);
    }
}
