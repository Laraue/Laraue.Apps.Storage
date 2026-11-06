using Laraue.Apps.Storage.DataAccess.Entities;

namespace Laraue.Apps.Storage.DataAccess.Data;

public static class ServicesData
{
    private static readonly Service Boards = new()
    {
        Id = ServiceId.LaraueBoards,
        Name = "Laraue Boards",
        Code = "laraue_boards",
    };

    public static readonly Service[] Services =
    [
        Boards,
    ];
}
