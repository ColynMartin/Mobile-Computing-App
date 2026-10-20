using SQLite;

namespace BookClubApp.App.Models;

public class CommentChange
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string CommentId { get; set; } = string.Empty;

    // Create, Update, or Delete.
    public string Operation { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // Preserve the comment's state when the change happened.
    public string SnapshotJson { get; set; } = string.Empty;

    public bool IsSynced { get; set; } = false;
}