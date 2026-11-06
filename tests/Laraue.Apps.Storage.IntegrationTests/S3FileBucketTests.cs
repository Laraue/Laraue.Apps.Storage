using Laraue.Apps.Storage.Services.Buckets;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.IntegrationTests;

/// <summary>
/// The upload request is built without a bucket. Writes and deletes against a real B2 bucket aren't covered here.
/// </summary>
public class S3FileBucketTests
{
    [Fact]
    public void CreatePutRequest_ShouldCarryDownloadHeaders_WhenObjectIsWritten()
    {
        using var bucket = new S3FileBucket(Options.Create(new FileBucketOptions
        {
            ServiceUrl = "https://s3.eu-central-003.backblazeb2.com",
            Region = "eu-central-003",
            BucketName = "laraue-files",
            KeyId = "key-id",
            ApplicationKey = "application-key",
        }));
        using var content = new MemoryStream([1, 2, 3]);

        var request = bucket.CreatePutRequest(
            "files/abc",
            content,
            new FileObjectHeaders("application/pdf", "attachment; filename=report.pdf", "public, max-age=31536000, immutable"));

        Assert.Equal("laraue-files", request.BucketName);
        Assert.Equal("files/abc", request.Key);
        Assert.Equal("application/pdf", request.ContentType);
        Assert.Equal("attachment; filename=report.pdf", request.Headers.ContentDisposition);
        Assert.Equal("public, max-age=31536000, immutable", request.Headers.CacheControl);
        Assert.False(request.UseChunkEncoding);
    }
}
