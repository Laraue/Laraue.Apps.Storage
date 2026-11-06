using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// <see cref="IFileBucket"/> over the S3-compatible API of a Backblaze B2 bucket (<see cref="FileBucketOptions"/>).
/// </summary>
public sealed class S3FileBucket : IFileBucket, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;

    public S3FileBucket(IOptions<FileBucketOptions> options)
    {
        var value = options.Value;
        _bucketName = value.BucketName;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(value.KeyId, value.ApplicationKey),
            new AmazonS3Config
            {
                ServiceURL = value.ServiceUrl,
                AuthenticationRegion = value.Region,
                ForcePathStyle = true,
                // B2 doesn't take the CRC checksum headers the SDK sends by default since 2025.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task Write(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = contentType,
                // Files are small (10 MB at most); a signed single payload works with every S3-compatible API.
                UseChunkEncoding = false,
            },
            cancellationToken);
    }

    public async Task<FileBucketObject> Read(string key, FileBucketRange? range, CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
        };

        if (range is not null)
            request.ByteRange = ToByteRange(range);

        GetObjectResponse response;
        try
        {
            response = await _client.GetObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var metadata = await _client.GetObjectMetadataAsync(_bucketName, key, cancellationToken);
            throw new FileBucketRangeNotSatisfiableException(metadata.ContentLength);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Object {key} is not in the bucket.", e);
        }

        var length = response.ContentLength;
        if (response.HttpStatusCode != HttpStatusCode.PartialContent
            || !TryParseContentRange(response.ContentRange, out var from, out var to, out var totalLength))
        {
            return new FileBucketObject(response.ResponseStream, 0, Math.Max(length - 1, 0), length, false);
        }

        return new FileBucketObject(response.ResponseStream, from, to, totalLength, true);
    }

    public async Task Delete(string key, CancellationToken cancellationToken)
    {
        // A B2 bucket keeps every version by default and DeleteObject without a version only hides the file, so
        // each version is deleted to free the space.
        var versions = await _client.ListVersionsAsync(
            new ListVersionsRequest
            {
                BucketName = _bucketName,
                Prefix = key,
            },
            cancellationToken);

        foreach (var version in versions.Versions ?? [])
        {
            if (version.Key != key)
                continue;

            await _client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    VersionId = version.VersionId,
                },
                cancellationToken);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static ByteRange ToByteRange(FileBucketRange range)
    {
        if (range.SuffixLength is { } suffix)
            return new ByteRange($"bytes=-{suffix}");

        return range.To is { } to
            ? new ByteRange(range.From ?? 0, to)
            : new ByteRange($"bytes={range.From ?? 0}-");
    }

    /// <summary>
    /// Parses <c>bytes 0-99/1000</c>.
    /// </summary>
    private static bool TryParseContentRange(string? contentRange, out long from, out long to, out long totalLength)
    {
        from = to = totalLength = 0;
        if (contentRange is null || !contentRange.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = contentRange["bytes ".Length..].Split('/', '-');
        return parts.Length == 3
            && long.TryParse(parts[0], out from)
            && long.TryParse(parts[1], out to)
            && long.TryParse(parts[2], out totalLength);
    }
}
