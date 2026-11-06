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

    public Task<string> GetDownloadUrl(string key, FileDownload download)
    {
        // Signed locally, no request to the bucket. The bucket answers byte ranges itself.
        return _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            Expires = download.ExpiresAt,
            ResponseHeaderOverrides =
            {
                ContentType = download.ContentType,
                ContentDisposition = download.ContentDisposition,
            },
        });
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
}
