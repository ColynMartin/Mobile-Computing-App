using SQLite;

namespace BookClubApp.App.Models;

public class PageComment
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string BookId { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    public string AuthorId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string CommentText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? ParentCommentId { get; set; }
}