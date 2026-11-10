using SQLite;

namespace BookClubApp.App.Models;

public class CommentChange
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string CommentId { get; set; } = string.Empty;

    // Create, Update, or Delete.
    public string Operation { get; set; } = string.Empty;

    // Server version this edit was based on.
    public int? BaseVersion { get; set; }

    // Previous pending local edit to this same comment.
    public string? PreviousChangeId { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // Preserve the comment's state when the change happened.
    public string SnapshotJson { get; set; } = string.Empty;

    public bool IsSynced { get; set; } = false;
}