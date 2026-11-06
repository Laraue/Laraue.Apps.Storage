using System.Net;
using System.Text.Json;
using Laraue.Apps.Storage.Services.Cdn;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.IntegrationTests;

public class CloudflareCdnCacheTests
{
    [Fact]
    public async Task PurgeAsync_ShouldPostUrlsInBatchesOf30_WhenManyUrlsArePurged()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var cache = CreateCache(handler);
        var urls = Enumerable.Range(0, 31).Select(i => $"https://files.laraue.com/file/b/files/{i}").ToArray();

        await cache.PurgeAsync(urls, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var (request, body) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.cloudflare.com/client/v4/zones/zone-1/purge_cache", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-1", request.Headers.Authorization.Parameter);
        Assert.Equal(30, JsonDocument.Parse(body).RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal(1, JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task PurgeAsync_ShouldThrow_WhenCloudflareRefuses()
    {
        var cache = CreateCache(new RecordingHandler(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => cache.PurgeAsync(["https://files.laraue.com/file/b/files/1"], CancellationToken.None));
    }

    private static CloudflareCdnCache CreateCache(HttpMessageHandler handler)
    {
        return new CloudflareCdnCache(
            new HttpClient(handler) { BaseAddress = new Uri(CloudflareCdnCache.BaseAddress) },
            Options.Create(new CloudflareOptions { ZoneId = "zone-1", ApiToken = "token-1" }));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(statusCode) { Content = new StringContent("{}") };
        }
    }
}
