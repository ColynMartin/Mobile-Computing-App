using BookClubApp.Api.Models;
using BookClubApp.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookClubApp.Api.Controllers;

[ApiController]
[Route("pullChanges")]
public class PullChangesController : ControllerBase
{
    private readonly ServerDatabase database;

    public PullChangesController(ServerDatabase database)
    {
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<PullChangesResponse>> PullChanges(
        [FromQuery] DateTimeOffset? since = null)
    {
        var connection = await database.GetConnectionAsync();
        var result = new PullChangesResponse();

        await connection.RunInTransactionAsync(transaction =>
        {
            // Capture the checkpoint while reading a consistent database state.
            result.SyncTimeUtc = DateTime.UtcNow;

            if (since is null)
            {
                // First pull: include all comments, including deleted records.
                result.Comments = transaction.Table<PageComment>().ToList();
                return;
            }

            DateTime lastSyncUtc = since.Value.UtcDateTime;

            result.Comments = transaction.Query<PageComment>(
                """
                SELECT c.*
                FROM PageComment AS c
                WHERE EXISTS (
                    SELECT 1
                    FROM ReceivedCommentChange AS change
                    WHERE change.CommentId = c.Id
                      AND change.ProcessedAt IS NOT NULL
                      AND change.ProcessedAt >= ?
                      AND change.ProcessedAt <= ?
                )
                """,
                lastSyncUtc,
                result.SyncTimeUtc);
        });

        return Ok(result);
    }
}