using SQLite;

namespace BookClubApp.Api.Models;

public class CommentAuditEntry
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string ChangeId { get; set; } = string.Empty;

    [Indexed]
    public string CommentId { get; set; } = string.Empty;

    // Create, Update, Delete, or a later resolution operation.
    public string Operation { get; set; } = string.Empty;

    // Describes whether the change was accepted or conflicted.
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int? BaseVersion { get; set; }

    public int? ServerVersionBefore { get; set; }

    public int? ServerVersionAfter { get; set; }

    // Preserve the incoming version and the server's previous version.
    public string IncomingSnapshotJson { get; set; } = string.Empty;

    public string? ServerSnapshotBeforeJson { get; set; }

    // Server time when the outcome was recorded.
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}