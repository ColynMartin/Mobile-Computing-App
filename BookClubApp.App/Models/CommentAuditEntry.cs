using SQLite;

namespace BookClubApp.App.Models;

public class CommentAuditEntry
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string ChangeId { get; set; } = string.Empty;

    [Indexed]
    public string CommentId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int? BaseVersion { get; set; }

    public int? ServerVersionBefore { get; set; }

    public int? ServerVersionAfter { get; set; }

    public string IncomingSnapshotJson { get; set; } = string.Empty;

    public string? ServerSnapshotBeforeJson { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}