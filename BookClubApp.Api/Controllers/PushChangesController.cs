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

        var connection = await database.GetConnectionAsync();
        var processedIds = new List<string>();

        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Id) ||
                string.IsNullOrWhiteSpace(change.CommentId) ||
                !new[] { "Create", "Update", "Delete" }.Contains(change.Operation))
            {
                return BadRequest("Invalid change information.");
            }

            try
            {
                var comment = JsonSerializer.Deserialize<PageComment>(
                    change.SnapshotJson);

                if (comment is null || comment.Id != change.CommentId)
                    return BadRequest("Invalid comment snapshot.");
            }
            catch (JsonException)
            {
                return BadRequest("Invalid JSON snapshot.");
            }
        }

        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var change in changes)
            {
                var existing = transaction.Find<ReceivedCommentChange>(change.Id);

                if (existing is not null)
                {
                    if (existing.ProcessedAt is not null)
                        processedIds.Add(change.Id);

                    continue;
                }

                var comment = JsonSerializer.Deserialize<PageComment>(
                    change.SnapshotJson)!;

                var received = new ReceivedCommentChange
                {
                    Id = change.Id,
                    CommentId = change.CommentId,
                    Operation = change.Operation,
                    ChangedAt = change.ChangedAt,
                    SnapshotJson = change.SnapshotJson,
                    ReceivedAt = DateTime.UtcNow
                };

                transaction.Insert(received);

                var storedComment = transaction.Find<PageComment>(comment.Id);

                if (storedComment is null)
                    transaction.Insert(comment);
                else
                    transaction.Update(comment);

                received.ProcessedAt = DateTime.UtcNow;
                transaction.Update(received);

                processedIds.Add(change.Id);
            }
        });

        return Ok(new { processedChangeIds = processedIds });
    }
}