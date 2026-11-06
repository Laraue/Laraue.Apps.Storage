using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Storage.Services.Buckets;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Storage.Services.Links;

/// <summary>
/// Signs and checks links to files the public web host serves from this server's disk:
/// <c>{base}/files/{area}/{key}?ct=..&amp;cd=..&amp;exp=..&amp;sig=..</c>. The signature covers the area, key, content
/// type, disposition and expiry, so none can be changed; the host needs no database to trust a link.
/// </summary>
public sealed class FileLinkSigner(IOptions<FileLinkOptions> options)
{
    /// <summary>
    /// The first path segment of a link, the public host's <c>FilesController</c> route.
    /// </summary>
    public const string RouteSegment = "files";

    public const string ContentTypeParameter = "ct";
    public const string DispositionParameter = "cd";
    public const string ExpiresParameter = "exp";
    public const string SignatureParameter = "sig";

    public string CreateUrl(string baseUrl, string area, string key, FileDownload download)
    {
        var expires = new DateTimeOffset(DateTime.SpecifyKind(download.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var signature = Sign(area, key, download.ContentType, download.ContentDisposition, expires);
        var path = string.Join('/', key.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

        return $"{baseUrl.TrimEnd('/')}/{RouteSegment}/{Uri.EscapeDataString(area)}/{path}"
            + $"?{ContentTypeParameter}={Uri.EscapeDataString(download.ContentType)}"
            + $"&{DispositionParameter}={Uri.EscapeDataString(download.ContentDisposition)}"
            + $"&{ExpiresParameter}={expires}"
            + $"&{SignatureParameter}={signature}";
    }

    /// <summary>
    /// Whether the link's values are what was signed and it hasn't expired at <paramref name="now"/>.
    /// </summary>
    public bool IsValid(
        string area,
        string key,
        string contentType,
        string contentDisposition,
        long expires,
        string signature,
        DateTime now)
    {
        if (DateTimeOffset.FromUnixTimeSeconds(expires) <= new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)))
            return false;

        var expected = Encoding.ASCII.GetBytes(Sign(area, key, contentType, contentDisposition, expires));
        var actual = Encoding.ASCII.GetBytes(signature);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(string area, string key, string contentType, string contentDisposition, long expires)
    {
        var payload = $"{area}\n{key.Replace('\\', '/')}\n{contentType}\n{contentDisposition}\n{expires}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.Value.SigningKey),
            Encoding.UTF8.GetBytes(payload));

        return WebEncoders.Base64UrlEncode(hash);
    }
}
