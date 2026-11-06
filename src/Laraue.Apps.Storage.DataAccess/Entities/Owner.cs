namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// Whoever files are stored for, separately per calling service, with the space its files take: an
/// <see cref="OrganizationOwner"/> or a <see cref="UserOwner"/> (one table, <see cref="Kind"/> tells them apart).
/// The row is locked while an upload or a delete changes <see cref="UsageBytes"/>, so two uploads can't both pass
/// the quota check.
/// </summary>
public abstract class Owner
{
    public long Id { get; set; }

    public OwnerKind Kind { get; set; }

    public ServiceId ServiceId { get; set; }
    public Service? Service { get; set; }

    /// <summary>
    /// The total <see cref="StoredFile.Size"/> of the owner's files, originals and previews.
    /// </summary>
    public long UsageBytes { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// An organization of the calling service (Boards: the organization a file is uploaded in).
/// </summary>
public sealed class OrganizationOwner : Owner
{
    /// <summary>
    /// The calling service's own organization id.
    /// </summary>
    public long OrganizationId { get; set; }
}

/// <summary>
/// A Laraue user, by the global user id from Identity.
/// </summary>
public sealed class UserOwner : Owner
{
    public Guid UserId { get; set; }
}

public enum OwnerKind
{
    Organization = 1,
    User = 2,
}
