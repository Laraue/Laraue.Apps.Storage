using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.Services.Links;

/// <summary>
/// Section <c>FileLinks</c>, the same in both hosts: the internal host signs links to files on this server's disk,
/// the public web host checks them.
/// </summary>
public class FileLinkOptions
{
    /// <summary>
    /// The HMAC-SHA256 key, at least 32 characters. Changing it breaks every link handed out before.
    /// </summary>
    [Required]
    [MinLength(32)]
    public required string SigningKey { get; set; }
}
