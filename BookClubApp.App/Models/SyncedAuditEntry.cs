using SQLite;

namespace BookClubApp.App.Models;

public class SyncedAuditEntry
{
    [PrimaryKey]
    public string AuditEntryId { get; set; } = string.Empty;

    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}