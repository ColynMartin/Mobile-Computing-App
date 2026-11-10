using SQLite;

namespace BookClubApp.App.Models;

public class CommentConflict
{
    // One conflict record per submitted change.
    [PrimaryKey]
    public string ChangeId { get; set; } = string.Empty;

    [Indexed]
    public string CommentId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int? BaseVersion { get; set; }

    public int? ServerVersion { get; set; }

    // Preserve the submitted edit and the server version separately.
    public string LocalSnapshotJson { get; set; } = string.Empty;

    public string? ServerSnapshotJson { get; set; }

    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set; } = false;

    public string? Resolution { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}