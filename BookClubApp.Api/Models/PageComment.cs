using SQLite;

namespace BookClubApp.Api.Models;

public class PageComment
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string BookId { get; set; } = string.Empty;
    public int PageNumber { get; set; }

    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string? ParentCommentId { get; set; }
    public bool IsDeleted { get; set; }
}