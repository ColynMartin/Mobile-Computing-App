namespace BookClubApp.Api.Models;

public class PushChangesResponse
{
    // Only accepted changes are acknowledged as successfully synced.
    public List<string> ProcessedChangeIds { get; set; } = new();

    // Detailed outcomes let the app handle conflicts separately.
    public List<PushChangeResult> Results { get; set; } = new();
}

public class PushChangeResult
{
    public string ChangeId { get; set; } = string.Empty;

    public string CommentId { get; set; } = string.Empty;

    // Accepted or Conflict.
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    // Server comment at the time this outcome was recorded.
    public PageComment? ServerComment { get; set; }

    // Audit evidence for this change.
    public CommentAuditEntry? AuditEntry { get; set; }
}