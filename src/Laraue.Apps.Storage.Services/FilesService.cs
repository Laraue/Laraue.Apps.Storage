using System.Security.Cryptography;
using Laraue.Apps.Storage.DataAccess;
using Laraue.Apps.Storage.DataAccess.Entities;
using Laraue.Apps.Storage.Services.Buckets;
using Laraue.Apps.Storage.Services.Cdn;
using Laraue.Apps.Storage.Services.Previews;
using Laraue.Apps.Storage.Services.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
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

public sealed record FileDetails(Guid Id, string FileName, string ContentType, long Size, string Url);

public abstract record UploadResult;

public sealed record FileUploaded(
    FileDetails File,
    FileDetails? Preview,
    Guid ReferenceId,
    long OwnerUsageBytes,
    bool Deduplicated)
    : UploadResult;

public sealed record QuotaExceeded(long OwnerUsageBytes, long QuotaBytes, long RequiredBytes) : UploadResult;

public sealed record ReferenceDeleteResult(bool FileDeleted, long OwnerUsageBytes);

public interface IFilesService
{
    /// <summary>
    /// Stores a file for <see cref="UploadData.Owner"/>, or reuses the same content already stored for it, and adds
    /// a pending reference to it. Throws <see cref="StorageValidationException"/> for an invalid request.
    /// </summary>
    Task<UploadResult> UploadAsync(ServiceId serviceId, UploadData data, CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="StoredFileNotFoundException"/> when the calling service has no such file.
    /// </summary>
    Task<FileDetails> GetAsync(ServiceId serviceId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a pending reference; a confirmed one stays as it is. Throws
    /// <see cref="FileReferenceNotFoundException"/> when it doesn't exist (any more).
    /// </summary>
    Task ConfirmReferenceAsync(ServiceId serviceId, Guid referenceId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a reference; deletes its file and preview when it was the last one. An unknown reference changes
    /// nothing, so retries are safe.
    /// </summary>
    Task<ReferenceDeleteResult> DeleteReferenceAsync(
        ServiceId serviceId,
        Guid referenceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> references still pending after
    /// <see cref="StorageOptions.PendingReferenceLifetime"/> (their files too when nothing else uses them) and
    /// returns how many. For every service.
    /// </summary>
    Task<int> DeleteExpiredPendingReferencesAsync(int batchSize, CancellationToken cancellationToken);

    Task<long> GetUsageAsync(ServiceId serviceId, OwnerRef owner, CancellationToken cancellationToken);
}

public sealed class FilesService(
    DatabaseContext context,
    IFileBucket bucket,
    ICdnCache cdnCache,
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

        var now = dateTimeProvider.UtcNow;

        if (existing is not null)
        {
            var reference = AddPendingReference(existing.Id, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            metrics.RecordUpload(StorageMetrics.UploadResults.Deduplicated);

            return new FileUploaded(
                ToDetails(existing),
                existing.Preview is null ? null : ToDetails(existing.Preview),
                reference.Id,
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

        var original = new StoredFile
        {
            Id = Guid.CreateVersion7(),
            OwnerId = owner.Id,
            Kind = StoredFileKind.Original,
            StorageKey = string.Empty,
            FileName = fileName,
            ContentType = contentType,
            Size = data.Content.Length,
            ContentHash = contentHash,
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

        FileReference newReference;
        await WriteContent(original, data.Content, cancellationToken);
        try
        {
            if (previewFile is not null)
                await WriteContent(previewFile, preview!, cancellationToken);

            context.StoredFiles.Add(original);
            if (previewFile is not null)
                context.StoredFiles.Add(previewFile);

            newReference = AddPendingReference(original.Id, now);

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
            newReference.Id,
            owner.UsageBytes,
            Deduplicated: false);
    }

    public async Task<FileDetails> GetAsync(ServiceId serviceId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await context.StoredFiles
            .Where(x => x.Id == fileId)
            .Where(x => x.Owner!.ServiceId == serviceId)
            .Select(x => new { x.Id, x.FileName, x.ContentType, x.Size, x.StorageKey })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);

        return new FileDetails(file.Id, file.FileName, file.ContentType, file.Size, GetUrl(file.StorageKey));
    }

    public async Task ConfirmReferenceAsync(
        ServiceId serviceId,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var reference = await context.FileReferences
            .Where(x => x.Id == referenceId)
            .Where(x => x.File!.Owner!.ServiceId == serviceId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new FileReferenceNotFoundException(referenceId);

        if (reference.State == FileReferenceState.Confirmed)
            return;

        reference.State = FileReferenceState.Confirmed;
        reference.ConfirmedAt = dateTimeProvider.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReferenceDeleteResult> DeleteReferenceAsync(
        ServiceId serviceId,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var reference = await context.FileReferences
            .Where(x => x.Id == referenceId)
            .Where(x => x.File!.Owner!.ServiceId == serviceId)
            .Select(x => new { x.File!.OwnerId, x.File.Owner!.UsageBytes })
            .FirstOrDefaultAsync(cancellationToken);

        if (reference is null)
            return new ReferenceDeleteResult(false, 0);

        return await DeleteReference(referenceId, reference.OwnerId, cancellationToken);
    }

    public async Task<int> DeleteExpiredPendingReferencesAsync(int batchSize, CancellationToken cancellationToken)
    {
        var createdBefore = dateTimeProvider.UtcNow - options.Value.PendingReferenceLifetime;

        var expired = await context.FileReferences
            .Where(x => x.State == FileReferenceState.Pending)
            .Where(x => x.CreatedAt < createdBefore)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .Select(x => new { x.Id, x.File!.OwnerId })
            .ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var reference in expired)
        {
            // Under the owner's lock, so a reference confirmed meanwhile is kept.
            var result = await DeleteReference(
                reference.Id,
                reference.OwnerId,
                cancellationToken,
                onlyIfPending: true);

            if (result is not null)
                deleted++;
        }

        return deleted;
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

    private FileReference AddPendingReference(Guid fileId, DateTime now)
    {
        var reference = new FileReference
        {
            Id = Guid.CreateVersion7(),
            FileId = fileId,
            State = FileReferenceState.Pending,
            CreatedAt = now,
        };
        context.FileReferences.Add(reference);

        return reference;
    }

    /// <summary>
    /// Deletes the reference under its owner's lock, and its file and preview when no other reference is left.
    /// Returns null when there was nothing to delete (already gone, or confirmed while <paramref name="onlyIfPending"/>).
    /// </summary>
    private async Task<ReferenceDeleteResult> DeleteReference(
        Guid referenceId,
        long ownerId,
        CancellationToken cancellationToken)
    {
        return await DeleteReference(referenceId, ownerId, cancellationToken, onlyIfPending: false)
            ?? new ReferenceDeleteResult(false, await context.Owners
                .Where(x => x.Id == ownerId)
                .Select(x => x.UsageBytes)
                .FirstOrDefaultAsync(cancellationToken));
    }

    private async Task<ReferenceDeleteResult?> DeleteReference(
        Guid referenceId,
        long ownerId,
        CancellationToken cancellationToken,
        bool onlyIfPending)
    {
        // The owner row must be read fresh under the lock: an entity tracked from an earlier call on this context
        // (the cleaner deletes many references in a row) would be returned with its old usage.
        context.ChangeTracker.Clear();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var owner = await context.Owners
            .FromSqlInterpolated($"select * from owners where id = {ownerId} for update")
            .AsTracking()
            .FirstAsync(cancellationToken);

        // Read again under the lock: a concurrent delete or confirm may have changed it.
        var reference = await context.FileReferences
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == referenceId, cancellationToken);

        if (reference is null || onlyIfPending && reference.State != FileReferenceState.Pending)
            return null;

        context.FileReferences.Remove(reference);

        var othersLeft = await context.FileReferences
            .Where(x => x.FileId == reference.FileId)
            .Where(x => x.Id != referenceId)
            .AnyAsync(cancellationToken);

        if (othersLeft)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ReferenceDeleteResult(false, owner.UsageBytes);
        }

        var original = await context.StoredFiles
            .AsTracking()
            .Include(x => x.Preview)
            .FirstAsync(x => x.Id == reference.FileId, cancellationToken);

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

        await PurgeFromCdn(preview is null ? [original] : [original, preview]);

        return new ReferenceDeleteResult(true, owner.UsageBytes);
    }

    private async Task WriteContent(StoredFile file, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var headers = new FileObjectHeaders(
                file.ContentType,
                GetContentDisposition(file.ContentType, file.FileName),
                ImmutableCacheControl);
            await bucket.Write(file.StorageKey, stream, headers, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            metrics.RecordBucketFailure(StorageMetrics.BucketOperations.Write);
            logger.LogError(e, "Couldn't write {Key}", file.StorageKey);
            throw;
        }
    }

    private async Task DeleteContent(StoredFile file)
    {
        try
        {
            await bucket.Delete(file.StorageKey, CancellationToken.None);
        }
        catch (Exception e)
        {
            metrics.RecordBucketFailure(StorageMetrics.BucketOperations.Delete);
            logger.LogError(e, "Couldn't delete {Key}; the content is left behind", file.StorageKey);
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

    /// <summary>
    /// The CDN forgets a deleted file at once; browsers that already downloaded it keep their copy.
    /// </summary>
    private async Task PurgeFromCdn(IReadOnlyCollection<StoredFile> files)
    {
        try
        {
            await cdnCache.PurgeAsync(files.Select(x => GetUrl(x.StorageKey)).ToArray(), CancellationToken.None);
        }
        catch (Exception e)
        {
            metrics.RecordBucketFailure(StorageMetrics.BucketOperations.Purge);
            logger.LogError(e, "Couldn't purge deleted files {FileIds} from the CDN", files.Select(x => x.Id));
        }
    }

    private string GetUrl(string key) => $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{key}";

    private FileDetails ToDetails(StoredFile file) =>
        new(file.Id, file.FileName, file.ContentType, file.Size, GetUrl(file.StorageKey));

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
    /// Objects never change - the key is the file id - so clients and the CDN keep them for a year.
    /// </summary>
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// Images and videos are shown (<c>inline</c>); anything else is saved, as <c>attachment</c> with its name in both
    /// forms browsers read (ASCII and RFC 5987 UTF-8). SVG is saved too: it can carry scripts.
    /// </summary>
    private static string GetContentDisposition(string contentType, string fileName)
    {
        var isShown = contentType.StartsWith("video/", StringComparison.Ordinal)
            || contentType.StartsWith("image/", StringComparison.Ordinal) && contentType != "image/svg+xml";
        if (isShown)
            return "inline";

        var value = new ContentDispositionHeaderValue("attachment");
        value.SetHttpFileName(fileName);

        return value.ToString();
    }

    private static string GetOriginalKey(Guid fileId) => $"files/{fileId:N}";

    private static string GetPreviewKey(Guid fileId) => $"previews/{fileId:N}.webp";

}
