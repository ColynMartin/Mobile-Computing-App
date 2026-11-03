using SQLite;

namespace BookClubApp.App.Models;

public class PullSyncState
{
    [PrimaryKey]
    public string Id { get; set; } = "pull";

    public DateTime? LastAppliedSyncTimeUtc { get; set; }

    public DateTime? PendingSyncTimeUtc { get; set; }
}