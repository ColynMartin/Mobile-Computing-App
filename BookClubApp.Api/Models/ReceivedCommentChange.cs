using SQLite;

namespace BookClubApp.Api.Models;

public class ReceivedCommentChange
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string CommentId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}