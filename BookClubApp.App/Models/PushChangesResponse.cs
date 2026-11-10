namespace BookClubApp.App.Models;

public class PushChangesResponse
{
    public List<string> ProcessedChangeIds { get; set; } = new();

    public List<PushChangeResult> Results { get; set; } = new();
}

public class PushChangeResult
{
    public string ChangeId { get; set; } = string.Empty;

    public string CommentId { get; set; } = string.Empty;

    // Accepted or Conflict.
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public PageComment? ServerComment { get; set; }

    public CommentAuditEntry? AuditEntry { get; set; }
}