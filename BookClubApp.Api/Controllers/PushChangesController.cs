using BookClubApp.Api.Models;
using BookClubApp.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BookClubApp.Api.Controllers;

[ApiController]
[Route("pushChanges")]
public class PushChangesController : ControllerBase
{
    private readonly ServerDatabase database;

    public PushChangesController(ServerDatabase database)
    {
        this.database = database;
    }

    [HttpPost]
    public async Task<IActionResult> PushChanges(
        [FromBody] List<ReceivedCommentChange> changes)
    {
        if (changes is null || changes.Count == 0)
            return BadRequest("No changes were provided.");

        var snapshots = new Dictionary<string, PageComment>();

        // Validate the entire request before changing the database.
        foreach (var change in changes)
        {
            if (change is null ||
                string.IsNullOrWhiteSpace(change.Id) ||
                string.IsNullOrWhiteSpace(change.CommentId) ||
                string.IsNullOrWhiteSpace(change.SnapshotJson) ||
                !new[] { "Create", "Update", "Delete" }
                    .Contains(change.Operation) ||
                change.BaseVersion < 0)
            {
                return BadRequest("Invalid change information.");
            }

            if (snapshots.ContainsKey(change.Id))
                return BadRequest("Duplicate change IDs in this request.");

            PageComment? comment;

            try
            {
                comment = JsonSerializer.Deserialize<PageComment>(
                    change.SnapshotJson);
            }
            catch (JsonException)
            {
                return BadRequest("Invalid JSON snapshot.");
            }

            if (comment is null ||
                comment.Id != change.CommentId ||
                string.IsNullOrWhiteSpace(comment.BookId) ||
                comment.PageNumber < 1)
            {
                return BadRequest("Invalid comment snapshot.");
            }

            bool isDelete = change.Operation == "Delete";

            if (comment.IsDeleted != isDelete)
                return BadRequest("Operation and deletion flag do not match.");

            if (!isDelete &&
                string.IsNullOrWhiteSpace(comment.CommentText))
            {
                return BadRequest("Comment text cannot be empty.");
            }

            snapshots.Add(change.Id, comment);
        }

        var connection = await database.GetConnectionAsync();
        var response = new PushChangesResponse();

        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var change in changes)
            {
                var existing = transaction
                    .Find<ReceivedCommentChange>(change.Id);

                // A retry must contain the same original change.
                if (existing is not null &&
                    (existing.CommentId != change.CommentId ||
                     existing.Operation != change.Operation ||
                     existing.BaseVersion != change.BaseVersion ||
                     existing.SnapshotJson != change.SnapshotJson))
                {
                    response.Results.Add(new PushChangeResult
                    {
                        ChangeId = change.Id,
                        CommentId = change.CommentId,
                        Outcome = "Conflict",
                        Reason = "This change ID was already used " +
                                 "with different content."
                    });

                    continue;
                }

                var previousAudit = transaction
                    .Table<CommentAuditEntry>()
                    .Where(entry => entry.ChangeId == change.Id)
                    .FirstOrDefault();

                // Return the original outcome without processing it twice.
                if (previousAudit is not null)
                {
                    AddAuditResult(response, previousAudit);
                    continue;
                }

                // Preserve acknowledgments from the earlier sync system.
                if (existing?.ProcessedAt is not null)
                {
                    response.ProcessedChangeIds.Add(change.Id);

                    response.Results.Add(new PushChangeResult
                    {
                        ChangeId = change.Id,
                        CommentId = change.CommentId,
                        Outcome = "Accepted",
                        Reason = "Previously processed before audit logging."
                    });

                    continue;
                }

                var incoming = snapshots[change.Id];
                var stored = transaction
                    .Find<PageComment>(change.CommentId);

                string? beforeJson = stored is null
                    ? null
                    : JsonSerializer.Serialize(stored);

                string? conflictReason =
                    GetConflictReason(change, stored);

                var received = existing ?? new ReceivedCommentChange
                {
                    Id = change.Id,
                    CommentId = change.CommentId,
                    Operation = change.Operation,
                    BaseVersion = change.BaseVersion,
                    ChangedAt = change.ChangedAt,
                    SnapshotJson = change.SnapshotJson,
                    ReceivedAt = DateTime.UtcNow
                };

                if (existing is null)
                    transaction.Insert(received);

                var audit = new CommentAuditEntry
                {
                    ChangeId = change.Id,
                    CommentId = change.CommentId,
                    Operation = change.Operation,
                    BaseVersion = change.BaseVersion,
                    ServerVersionBefore = stored?.Version,
                    IncomingSnapshotJson = change.SnapshotJson,
                    ServerSnapshotBeforeJson = beforeJson,
                    RecordedAtUtc = DateTime.UtcNow
                };

                if (conflictReason is not null)
                {
                    // Keep the current server comment unchanged.
                    audit.Outcome = "Conflict";
                    audit.Reason = conflictReason;
                    audit.ServerVersionAfter = stored?.Version;
                }
                else
                {
                    // The server owns the version number.
                    incoming.Version = checked((stored?.Version ?? 0) + 1);

                    if (stored is null)
                        transaction.Insert(incoming);
                    else
                        transaction.Update(incoming);

                    received.ProcessedAt = DateTime.UtcNow;
                    transaction.Update(received);

                    audit.Outcome = "Accepted";
                    audit.Reason = "Change accepted.";
                    audit.ServerVersionAfter = incoming.Version;
                }

                // Save the outcome in the same transaction as the change.
                transaction.Insert(audit);
                AddAuditResult(response, audit);
            }
        });

        return Ok(response);
    }

    private static string? GetConflictReason(
        ReceivedCommentChange change,
        PageComment? stored)
    {
        if (change.Operation == "Create")
        {
            if (stored is not null)
                return "A comment with this ID already exists.";

            // Legacy creates are safe when the ID does not exist.
            if (change.BaseVersion is not null &&
                change.BaseVersion != 0)
            {
                return "A new comment must start at version zero.";
            }

            return null;
        }

        if (stored is null)
            return "The comment no longer exists on the server.";

        if (change.BaseVersion is null)
        {
            return "This older change has no base version " +
                   "and requires review.";
        }

        if (change.BaseVersion != stored.Version)
        {
            return $"This edit used version {change.BaseVersion}, " +
                   $"but the server has version {stored.Version}.";
        }

        if (stored.IsDeleted)
            return "The comment has already been deleted on the server.";

        return null;
    }

    private static void AddAuditResult(
        PushChangesResponse response,
        CommentAuditEntry audit)
    {
        PageComment? serverComment = null;

        if (audit.Outcome == "Accepted")
        {
            serverComment = JsonSerializer.Deserialize<PageComment>(
                audit.IncomingSnapshotJson);

            if (serverComment is not null &&
                audit.ServerVersionAfter is int version)
            {
                serverComment.Version = version;
            }

            response.ProcessedChangeIds.Add(audit.ChangeId);
        }
        else if (audit.ServerSnapshotBeforeJson is not null)
        {
            serverComment = JsonSerializer.Deserialize<PageComment>(
                audit.ServerSnapshotBeforeJson);
        }

        response.Results.Add(new PushChangeResult
        {
            ChangeId = audit.ChangeId,
            CommentId = audit.CommentId,
            Outcome = audit.Outcome,
            Reason = audit.Reason,
            ServerComment = serverComment,
            AuditEntry = audit
        });
    }
}