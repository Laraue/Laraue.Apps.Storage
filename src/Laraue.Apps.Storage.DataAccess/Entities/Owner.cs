using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// Whoever files are stored for, as the calling service names it (<see cref="Key"/>, e.g. Boards'
/// "organization:42"), with the space its files take. The row is locked while an upload or a delete
/// changes <see cref="UsageBytes"/>, so two uploads can't both pass the quota check.
/// </summary>
public class Owner
{
    public const int KeyMaxLength = 128;

    public long Id { get; set; }

    public ServiceId ServiceId { get; set; }
    public Service? Service { get; set; }

    [MaxLength(KeyMaxLength)]
    public required string Key { get; set; }

    /// <summary>
    /// The total <see cref="StoredFile.Size"/> of the owner's files, originals and previews.
    /// </summary>
    public long UsageBytes { get; set; }

    public DateTime CreatedAt { get; set; }
}
