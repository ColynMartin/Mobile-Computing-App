using SQLite;

namespace BookClubApp.App.Models;

public class IncomingComment
{
    [PrimaryKey]
    public string CommentId { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;

    public DateTime DownloadedAtUtc { get; set; }

    public DateTime SyncTimeUtc { get; set; }
    public string? LocalSnapshotJson { get; set; }
}
