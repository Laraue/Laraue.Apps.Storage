using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.Services.Cdn;

/// <summary>
/// Purges by URL through the Cloudflare API (<c>POST /zones/{zone}/purge_cache</c>, at most 30 URLs a call).
/// </summary>
public sealed class CloudflareCdnCache(HttpClient httpClient, IOptions<CloudflareOptions> options) : ICdnCache
{
    public const string BaseAddress = "https://api.cloudflare.com/client/v4/";
    private const int MaxUrlsPerCall = 30;

    public async Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken)
    {
        foreach (var chunk in urls.Chunk(MaxUrlsPerCall))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"zones/{options.Value.ZoneId}/purge_cache")
            {
                Content = JsonContent.Create(new { files = chunk }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
}
