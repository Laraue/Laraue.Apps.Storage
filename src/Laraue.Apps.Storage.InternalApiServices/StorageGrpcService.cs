using Google.Protobuf;
using Grpc.Core;
using Laraue.Apps.Storage.Services;
using Laraue.Apps.Storage.Services.Buckets;
// The generated proto types share names with the domain ones (ServiceId, StoredFile, QuotaExceeded) - alias the
// contract side to keep both usable here.
using Contracts = Laraue.Apps.Storage.Internal.Contracts;
using Domain = Laraue.Apps.Storage.DataAccess.Entities;

namespace Laraue.Apps.Storage.InternalApiServices;

/// <summary>
/// gRPC-facing adapter over <see cref="IFilesService"/>: translates between the wire contract
/// (<c>Laraue.Apps.Storage.Internal.Contracts</c>) and the domain types. No business logic of its own.
/// </summary>
public sealed class StorageGrpcService(IFilesService filesService)
    : Contracts.StorageService.StorageServiceBase
{
    /// <summary>
    /// The size of each data message <see cref="ReadFile"/> sends.
    /// </summary>
    public const int ChunkSize = 64 * 1024;

    public override async Task<Contracts.UploadFileResponse> UploadFile(
        Contracts.UploadFileRequest request,
        ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);
        var data = new UploadData(
            ToOwnerRef(request.Owner),
            ParseOptionalUserId(request.UserId),
            request.FileName,
            request.ContentType,
            request.Content.ToByteArray(),
            request.HasQuotaBytes ? request.QuotaBytes : null);

        var result = await ExecuteAsync(() => filesService.UploadAsync(serviceId, data, context.CancellationToken));

        return result switch
        {
            FileUploaded uploaded => new Contracts.UploadFileResponse
            {
                Uploaded = ToContractUploaded(uploaded),
            },
            QuotaExceeded exceeded => new Contracts.UploadFileResponse
            {
                QuotaExceeded = new Contracts.QuotaExceeded
                {
                    OwnerUsageBytes = exceeded.OwnerUsageBytes,
                    QuotaBytes = exceeded.QuotaBytes,
                    RequiredBytes = exceeded.RequiredBytes,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }

    public override async Task<Contracts.StoredFile> GetFile(Contracts.GetFileRequest request, ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);
        var fileId = ParseFileId(request.FileId);

        var file = await ExecuteAsync(() => filesService.GetAsync(serviceId, fileId, context.CancellationToken));

        return ToContractFile(file);
    }

    public override async Task ReadFile(
        Contracts.ReadFileRequest request,
        IServerStreamWriter<Contracts.ReadFileResponse> responseStream,
        ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);
        var fileId = ParseFileId(request.FileId);
        var range = ToRange(request);

        var result = await ExecuteAsync(() => filesService.ReadAsync(serviceId, fileId, range, context.CancellationToken));
        await using var content = result.Content;

        await responseStream.WriteAsync(new Contracts.ReadFileResponse
        {
            Header = new Contracts.ReadFileHeader
            {
                File = ToContractFile(result.File),
                From = result.From,
                To = result.To,
                TotalLength = result.TotalLength,
                Partial = result.IsPartial,
            },
        });

        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await content.ReadAsync(buffer, context.CancellationToken)) > 0)
        {
            await responseStream.WriteAsync(new Contracts.ReadFileResponse
            {
                Data = ByteString.CopyFrom(buffer, 0, read),
            });
        }
    }

    public override async Task<Contracts.DeleteFileResponse> DeleteFile(
        Contracts.DeleteFileRequest request,
        ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);
        var fileId = ParseFileId(request.FileId);

        var result = await ExecuteAsync(() => filesService.DeleteAsync(serviceId, fileId, context.CancellationToken));

        return new Contracts.DeleteFileResponse
        {
            Deleted = result.Deleted,
            OwnerUsageBytes = result.OwnerUsageBytes,
        };
    }

    public override async Task<Contracts.GetUsageResponse> GetUsage(
        Contracts.GetUsageRequest request,
        ServerCallContext context)
    {
        var serviceId = ReadDomainServiceId(context);

        var usage = await ExecuteAsync(() => filesService.GetUsageAsync(serviceId, ToOwnerRef(request.Owner), context.CancellationToken));

        return new Contracts.GetUsageResponse { UsageBytes = usage };
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (StorageValidationException e)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
        catch (StoredFileNotFoundException e)
        {
            throw new RpcException(new Status(StatusCode.NotFound, e.Message));
        }
        catch (FileBucketRangeNotSatisfiableException e)
        {
            throw new RpcException(new Status(StatusCode.OutOfRange, e.Message), new Metadata
            {
                { "total-length", e.TotalLength.ToString() },
            });
        }
    }

    private static OwnerRef ToOwnerRef(Contracts.Owner? owner) => owner?.KindCase switch
    {
        Contracts.Owner.KindOneofCase.OrganizationId => new OrganizationOwnerRef(owner.OrganizationId),
        Contracts.Owner.KindOneofCase.UserId => Guid.TryParse(owner.UserId, out var userId)
            ? new UserOwnerRef(userId)
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid owner user id '{owner.UserId}'.")),
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "The owner is required.")),
    };

    private static FileBucketRange? ToRange(Contracts.ReadFileRequest request)
    {
        if (request.HasSuffixLength)
            return new FileBucketRange(null, null, request.SuffixLength);

        if (!request.HasRangeFrom && !request.HasRangeTo)
            return null;

        return new FileBucketRange(
            request.HasRangeFrom ? request.RangeFrom : null,
            request.HasRangeTo ? request.RangeTo : null);
    }

    private static Contracts.UploadedFile ToContractUploaded(FileUploaded uploaded)
    {
        var result = new Contracts.UploadedFile
        {
            File = ToContractFile(uploaded.File),
            OwnerUsageBytes = uploaded.OwnerUsageBytes,
            Deduplicated = uploaded.Deduplicated,
        };

        if (uploaded.Preview is not null)
            result.Preview = ToContractFile(uploaded.Preview);

        return result;
    }

    private static Contracts.StoredFile ToContractFile(FileDetails file) => new()
    {
        Id = file.Id.ToString(),
        FileName = file.FileName,
        ContentType = file.ContentType,
        Size = file.Size,
    };

    private static Guid ParseFileId(string fileId)
    {
        return Guid.TryParse(fileId, out var parsed)
            ? parsed
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid file id '{fileId}'."));
    }

    private static Guid? ParseOptionalUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        return Guid.TryParse(userId, out var parsed)
            ? parsed
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid user id '{userId}'."));
    }

    /// <summary>
    /// The calling service, from the <see cref="Contracts.GrpcHeaders.ServiceIdHeaderName"/> header.
    /// </summary>
    private static Domain.ServiceId ReadDomainServiceId(ServerCallContext context)
    {
        var header = context.RequestHeaders.Get(Contracts.GrpcHeaders.ServiceIdHeaderName)?.Value;
        if (header is null || !int.TryParse(header, out var rawServiceId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Missing or invalid '{Contracts.GrpcHeaders.ServiceIdHeaderName}' header."));
        }

        return (Contracts.ServiceId)rawServiceId switch
        {
            Contracts.ServiceId.LaraueBoards => Domain.ServiceId.LaraueBoards,
            var unknown => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown service '{unknown}'.")),
        };
    }
}
