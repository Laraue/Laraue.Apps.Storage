using System.Security.Cryptography;
using Laraue.Apps.Storage.DataAccess;
using Laraue.Apps.Storage.DataAccess.Entities;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Previews;
using Laraue.Apps.Storage.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Whoever a file is stored for: <see cref="OrganizationOwnerRef"/> or <see cref="UserOwnerRef"/>.
/// </summary>
public abstract record OwnerRef;

/// <summary>
/// An organization of the calling service, by the service's own id for it.
/// </summary>
public sealed record OrganizationOwnerRef(long OrganizationId) : OwnerRef;

/// <summary>
/// A Laraue user, by the global user id from Identity.
/// </summary>
public sealed record UserOwnerRef(Guid UserId) : OwnerRef;

public sealed record UploadData(
    OwnerRef Owner,
    Guid? UserId,
    string FileName,
    string ContentType,
    byte[] Content,
    long? QuotaBytes);

public sealed record FileDetails(Guid Id, string FileName, string ContentType, long Size);

public abstract record UploadResult;

public sealed record FileUploaded(FileDetails File, FileDetails? Preview, long OwnerUsageBytes, bool Deduplicated)
    : UploadResult;

public sealed record QuotaExceeded(long OwnerUsageBytes, long QuotaBytes, long RequiredBytes) : UploadResult;

public sealed record FileDeleteResult(bool Deleted, long OwnerUsageBytes);

/// <summary>
/// Where a client downloads a file: redirect to <see cref="Url"/> until <see cref="ExpiresAt"/>.
/// </summary>
public sealed record FileDownloadLocation(FileDetails File, string Url, DateTime ExpiresAt);

public interface IFilesService
{
    /// <summary>
    /// Stores a file for <see cref="UploadData.Owner"/>, or adds a reference to the same content already stored
    /// for it. Throws <see cref="StorageValidationException"/> for an invalid request.
    /// </summary>
    Task<UploadResult> UploadAsync(ServiceId serviceId, UploadData data, CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="StoredFileNotFoundException"/> when the calling service has no such file.
    /// </summary>
    Task<FileDetails> GetAsync(ServiceId serviceId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Where to download the file from, saved as <paramref name="downloadFileName"/> (the stored name when null)
    /// when <paramref name="attachment"/>. Throws <see cref="StoredFileNotFoundException"/>.
    /// </summary>
    Task<FileDownloadLocation> GetDownloadLocationAsync(
        ServiceId serviceId,
        Guid fileId,
        bool attachment,
        string? downloadFileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops one reference to an original; deletes it and its preview when none is left. An unknown file or a
    /// preview id changes nothing.
    /// </summary>
    Task<FileDeleteResult> DeleteAsync(ServiceId serviceId, Guid fileId, CancellationToken cancellationToken);

    Task<long> GetUsageAsync(ServiceId serviceId, OwnerRef owner, CancellationToken cancellationToken);
}

public sealed class FilesService(
    DatabaseContext context,
    [FromKeyedServices(BucketKeys.Originals)] IFileBucket originals,
    [FromKeyedServices(BucketKeys.Previews)] IFileBucket previews,
    IPreviewMaker previewMaker,
    IDateTimeProvider dateTimeProvider,
    IOptions<StorageOptions> options,
    StorageMetrics metrics,
    ILogger<FilesService> logger)
    : IFilesService
{
    public async Task<UploadResult> UploadAsync(
        ServiceId serviceId,
        UploadData data,
        CancellationToken cancellationToken)
    {
        ValidateOwner(data.Owner);
        var contentType = ValidateContentType(data.ContentType);
        var fileName = NormalizeFileName(data.FileName);

        if (data.Content.Length == 0)
            throw new StorageValidationException(Errors.FileEmpty);

        if (data.Content.Length > options.Value.MaxFileSizeBytes)
            throw new StorageValidationException(
                string.Format(Errors.FileTooLarge, data.Content.Length, options.Value.MaxFileSizeBytes));

        if (data.QuotaBytes < 0)
            throw new StorageValidationException(Errors.QuotaNegative);

        var contentHash = Convert.ToHexStringLower(SHA256.HashData(data.Content));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var owner = await LockOwner(serviceId, data.Owner, cancellationToken);

        var existing = await context.StoredFiles
            .Where(x => x.OwnerId == owner.Id)
            .Where(x => x.Kind == StoredFileKind.Original)
            .Where(x => x.ContentHash == contentHash)
            .Include(x => x.Preview)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            existing.References++;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            metrics.RecordUpload(StorageMetrics.UploadResults.Deduplicated);

            return new FileUploaded(
                ToDetails(existing),
                existing.Preview is null ? null : ToDetails(existing.Preview),
                owner.UsageBytes,
                Deduplicated: true);
        }

        var preview = previewMaker.TryMake(data.Content);
        var requiredBytes = data.Content.Length + (preview?.Length ?? 0);

        if (data.QuotaBytes is { } quota && owner.UsageBytes + requiredBytes > quota)
        {
            await transaction.RollbackAsync(cancellationToken);
            metrics.RecordUpload(StorageMetrics.UploadResults.QuotaExceeded);

            return new QuotaExceeded(owner.UsageBytes, quota, requiredBytes);
        }

        var now = dateTimeProvider.UtcNow;
        var original = new StoredFile
        {
            Id = Guid.CreateVersion7(),
            OwnerId = owner.Id,
            Kind = StoredFileKind.Original,
            Location = StoredFileLocation.Bucket,
            StorageKey = string.Empty,
            FileName = fileName,
            ContentType = contentType,
            Size = data.Content.Length,
            ContentHash = contentHash,
            References = 1,
            UploadedByUserId = data.UserId,
            CreatedAt = now,
        };
        original.StorageKey = GetOriginalKey(original.Id);

        StoredFile? previewFile = null;
        if (preview is not null)
        {
            previewFile = new StoredFile
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner.Id,
                Kind = StoredFileKind.Preview,
                Location = StoredFileLocation.LocalDisk,
                StorageKey = string.Empty,
                FileName = Path.ChangeExtension(fileName, ".webp"),
                ContentType = PreviewMaker.ContentType,
                Size = preview.Length,
                SourceFileId = original.Id,
                UploadedByUserId = data.UserId,
                CreatedAt = now,
            };
            previewFile.StorageKey = GetPreviewKey(previewFile.Id);
        }

        await WriteContent(original, data.Content, cancellationToken);
        try
        {
            if (previewFile is not null)
                await WriteContent(previewFile, preview!, cancellationToken);

            context.StoredFiles.Add(original);
            if (previewFile is not null)
                context.StoredFiles.Add(previewFile);

            owner.UsageBytes += requiredBytes;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Nothing points at the written content now; don't leave it taking space.
            await DeleteContent(original);
            if (previewFile is not null)
                await DeleteContent(previewFile);

            throw;
        }

        metrics.RecordUpload(StorageMetrics.UploadResults.Stored);

        return new FileUploaded(
            ToDetails(original),
            previewFile is null ? null : ToDetails(previewFile),
            owner.UsageBytes,
            Deduplicated: false);
    }

    public async Task<FileDetails> GetAsync(ServiceId serviceId, Guid fileId, CancellationToken cancellationToken)
    {
        return await context.StoredFiles
            .Where(x => x.Id == fileId)
            .Where(x => x.Owner!.ServiceId == serviceId)
            .Select(x => new FileDetails(x.Id, x.FileName, x.ContentType, x.Size))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);
    }

    public async Task<FileDownloadLocation> GetDownloadLocationAsync(
        ServiceId serviceId,
        Guid fileId,
        bool attachment,
        string? downloadFileName,
        CancellationToken cancellationToken)
    {
        var file = await context.StoredFiles
            .Where(x => x.Id == fileId)
            .Where(x => x.Owner!.ServiceId == serviceId)
            .Select(x => new { x.Id, x.FileName, x.ContentType, x.Size, x.Location, x.StorageKey })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);

        var details = new FileDetails(file.Id, file.FileName, file.ContentType, file.Size);

        var expiresAt = file.Location == StoredFileLocation.LocalDisk
            ? GetPreviewLinkExpiry(dateTimeProvider.UtcNow)
            : dateTimeProvider.UtcNow + options.Value.DownloadUrlLifetime;
        var download = new FileDownload(
            file.ContentType,
            GetContentDisposition(attachment, downloadFileName ?? file.FileName),
            expiresAt);

        var url = await BucketFor(file.Location).GetDownloadUrl(file.StorageKey, download);

        return new FileDownloadLocation(details, url, expiresAt);
    }

    public async Task<FileDeleteResult> DeleteAsync(
        ServiceId serviceId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var file = await context.StoredFiles
            .Where(x => x.Id == fileId)
            .Where(x => x.Owner!.ServiceId == serviceId)
            .Select(x => new { x.OwnerId, x.Kind, x.Owner!.UsageBytes })
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
            return new FileDeleteResult(false, 0);

        if (file.Kind != StoredFileKind.Original)
            return new FileDeleteResult(false, file.UsageBytes);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var owner = await context.Owners
            .FromSqlInterpolated($"select * from owners where id = {file.OwnerId} for update")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // Read again under the owner's lock: a concurrent delete may have dropped it already.
        var original = await context.StoredFiles
            .Include(x => x.Preview)
            .FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);

        if (original is null || owner is null)
            return new FileDeleteResult(false, owner?.UsageBytes ?? 0);

        original.References--;
        if (original.References > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new FileDeleteResult(false, owner.UsageBytes);
        }

        var preview = original.Preview;
        owner.UsageBytes = Math.Max(0, owner.UsageBytes - original.Size - (preview?.Size ?? 0));
        if (preview is not null)
            context.StoredFiles.Remove(preview);

        context.StoredFiles.Remove(original);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // After the commit: if this fails, the bytes are left behind but nothing refers to them any more.
        await DeleteContent(original);
        if (preview is not null)
            await DeleteContent(preview);

        return new FileDeleteResult(true, owner.UsageBytes);
    }

    public async Task<long> GetUsageAsync(ServiceId serviceId, OwnerRef owner, CancellationToken cancellationToken)
    {
        ValidateOwner(owner);

        return owner switch
        {
            OrganizationOwnerRef organization => await context.Owners
                .OfType<OrganizationOwner>()
                .Where(x => x.ServiceId == serviceId)
                .Where(x => x.OrganizationId == organization.OrganizationId)
                .Select(x => x.UsageBytes)
                .FirstOrDefaultAsync(cancellationToken),
            UserOwnerRef user => await context.Owners
                .OfType<UserOwner>()
                .Where(x => x.ServiceId == serviceId)
                .Where(x => x.UserId == user.UserId)
                .Select(x => x.UsageBytes)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, null),
        };
    }

    /// <summary>
    /// Creates the owner's row if it's new and locks it for the rest of the transaction, so uploads and deletes for
    /// one owner change its usage one at a time. The "on conflict" targets match the filtered unique indexes in
    /// <c>DatabaseContext</c>.
    /// </summary>
    private async Task<Owner> LockOwner(ServiceId serviceId, OwnerRef owner, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        switch (owner)
        {
            case OrganizationOwnerRef organization:
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     insert into owners (kind, service_id, organization_id, usage_bytes, created_at)
                     values ({(int)OwnerKind.Organization}, {(int)serviceId}, {organization.OrganizationId}, 0, {now})
                     on conflict (service_id, organization_id) where organization_id is not null do nothing
                     """,
                    cancellationToken);

                return await context.Owners
                    .FromSqlInterpolated(
                        $"""
                         select * from owners
                         where service_id = {(int)serviceId} and organization_id = {organization.OrganizationId}
                         for update
                         """)
                    .AsTracking()
                    .SingleAsync(cancellationToken);

            case UserOwnerRef user:
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     insert into owners (kind, service_id, user_id, usage_bytes, created_at)
                     values ({(int)OwnerKind.User}, {(int)serviceId}, {user.UserId}, 0, {now})
                     on conflict (service_id, user_id) where user_id is not null do nothing
                     """,
                    cancellationToken);

                return await context.Owners
                    .FromSqlInterpolated(
                        $"""
                         select * from owners
                         where service_id = {(int)serviceId} and user_id = {user.UserId}
                         for update
                         """)
                    .AsTracking()
                    .SingleAsync(cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(owner), owner, null);
        }
    }

    private IFileBucket BucketFor(StoredFileLocation location) => location switch
    {
        StoredFileLocation.Bucket => originals,
        StoredFileLocation.LocalDisk => previews,
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private async Task WriteContent(StoredFile file, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            await BucketFor(file.Location).Write(file.StorageKey, stream, file.ContentType, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            metrics.RecordBucketFailure(StorageMetrics.BucketOperations.Write);
            logger.LogError(e, "Couldn't write {Key} ({Location})", file.StorageKey, file.Location);
            throw;
        }
    }

    private async Task DeleteContent(StoredFile file)
    {
        try
        {
            await BucketFor(file.Location).Delete(file.StorageKey, CancellationToken.None);
        }
        catch (Exception e)
        {
            metrics.RecordBucketFailure(StorageMetrics.BucketOperations.Delete);
            logger.LogError(e, "Couldn't delete {Key} ({Location}); the content is left behind", file.StorageKey, file.Location);
        }
    }

    private static void ValidateOwner(OwnerRef? owner)
    {
        var isValid = owner switch
        {
            OrganizationOwnerRef organization => organization.OrganizationId > 0,
            UserOwnerRef user => user.UserId != Guid.Empty,
            _ => false,
        };

        if (!isValid)
            throw new StorageValidationException(Errors.OwnerInvalid);
    }

    private static string ValidateContentType(string contentType)
    {
        var value = contentType.Trim();
        if (value.Length == 0 || value.Length > StoredFile.ContentTypeMaxLength || !value.Contains('/'))
            throw new StorageValidationException(Errors.ContentTypeInvalid);

        return value;
    }

    /// <summary>
    /// The caller's name without directories or control characters, cut to the column's length with the extension
    /// kept. Callers sanitize names for their own use; this only keeps the stored one safe.
    /// </summary>
    private static string NormalizeFileName(string fileName)
    {
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
            name = "file";

        if (name.Length > StoredFile.FileNameMaxLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 16)
                extension = string.Empty;

            name = name[..(StoredFile.FileNameMaxLength - extension.Length)] + extension;
        }

        return name;
    }

    /// <summary>
    /// The end of tomorrow (UTC): a preview's link is the same all day, so browsers cache it while a board is
    /// re-rendered, and it still works for at least a day.
    /// </summary>
    private static DateTime GetPreviewLinkExpiry(DateTime now) => DateTime.SpecifyKind(now.Date.AddDays(2), DateTimeKind.Utc);

    /// <summary>
    /// <c>inline</c>, or <c>attachment</c> with the name in both forms browsers read (ASCII and RFC 5987 UTF-8).
    /// </summary>
    private static string GetContentDisposition(bool attachment, string fileName)
    {
        if (!attachment)
            return "inline";

        var value = new ContentDispositionHeaderValue("attachment");
        value.SetHttpFileName(NormalizeFileName(fileName));

        return value.ToString();
    }

    private static string GetOriginalKey(Guid fileId) => $"files/{fileId:N}";

    /// <summary>
    /// Two levels of folders by the id's random end, so no folder grows past a few thousand entries.
    /// </summary>
    private static string GetPreviewKey(Guid fileId)
    {
        var id = fileId.ToString("N");
        return Path.Combine(id[^2..], id[^4..^2], $"{id}.webp");
    }

    private static FileDetails ToDetails(StoredFile file) => new(file.Id, file.FileName, file.ContentType, file.Size);
}
