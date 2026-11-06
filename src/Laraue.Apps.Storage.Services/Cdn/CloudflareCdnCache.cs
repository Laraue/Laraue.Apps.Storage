using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.Services.Cdn;

/// <summary>
/// Purges by URL through the Cloudflare API (<c>POST /zones/{zone}/purge_cache</c>, at most 30 URLs a call). The
/// client is set up by <see cref="ConfigureClient"/>.
/// </summary>
public sealed class CloudflareCdnCache(HttpClient httpClient, IOptions<CloudflareOptions> options)
    : ICdnCache
{
    private const int MaxUrlsPerCall = 30;

    /// <summary>
    /// The API address and the token, once for the client instead of on every request.
    /// </summary>
    public static void ConfigureClient(HttpClient client, CloudflareOptions options)
    {
        client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
    }

    public async Task PurgeAsync(IReadOnlyCollection<string> urls, CancellationToken cancellationToken)
    {
        foreach (var chunk in urls.Chunk(MaxUrlsPerCall))
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"zones/{options.Value.ZoneId}/purge_cache",
                new { files = chunk },
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }
    }
}
