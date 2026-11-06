using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Storage.DataAccess.Entities;

/// <summary>
/// A Laraue app that keeps files here. An independent registry from Identity's, Billing's and
/// Feedback's own <c>ServiceId</c> enums, even though the numeric values start the same way.
/// </summary>
public class Service
{
    public ServiceId Id { get; set; }

    [MaxLength(32)]
    public required string Code { get; set; }

    [MaxLength(32)]
    public required string Name { get; set; }
}

public enum ServiceId
{
    LaraueBoards = 1,
}
