using System.Web;
using Laraue.Apps.Storage.Services.Buckets;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.IntegrationTests;

/// <summary>
/// Signing a link is computed locally, so it's tested without a bucket. Writes and deletes against a real B2
/// bucket aren't covered here.
/// </summary>
public class S3FileBucketTests
{
    [Fact]
    public async Task GetDownloadUrl_ShouldSignLinkWithResponseHeaders_WhenAsked()
    {
        using var bucket = new S3FileBucket(Options.Create(new FileBucketOptions
        {
            ServiceUrl = "https://s3.eu-central-003.backblazeb2.com",
            Region = "eu-central-003",
            BucketName = "laraue-files",
            KeyId = "key-id",
            ApplicationKey = "application-key",
        }));

        var url = new Uri(await bucket.GetDownloadUrl(
            "files/abc",
            new FileDownload("application/pdf", "attachment; filename=report.pdf", DateTime.UtcNow.AddMinutes(30))));

        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("https", url.Scheme);
        Assert.Equal("s3.eu-central-003.backblazeb2.com", url.Host);
        Assert.Equal("/laraue-files/files/abc", url.AbsolutePath);
        Assert.Equal("application/pdf", query["response-content-type"]);
        Assert.Equal("attachment; filename=report.pdf", query["response-content-disposition"]);
        Assert.NotNull(query["X-Amz-Signature"]);
        Assert.Contains("key-id", query["X-Amz-Credential"]);
        Assert.Contains("eu-central-003", query["X-Amz-Credential"]);
    }
}
