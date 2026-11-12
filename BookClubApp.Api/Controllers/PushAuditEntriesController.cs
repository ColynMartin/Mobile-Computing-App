using BookClubApp.Api.Models;
using BookClubApp.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookClubApp.Api.Controllers;

[ApiController]
[Route("pushAuditEntries")]
public class PushAuditEntriesController : ControllerBase
{
    private readonly ServerDatabase database;

    public PushAuditEntriesController(ServerDatabase database)
    {
        this.database = database;
    }

    [HttpPost]
    public async Task<IActionResult> PushAuditEntries([FromBody] List<CommentAuditEntry> entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return BadRequest("No audit entries were provided.");
        }

        var ids = new HashSet<string>();

        // Validate the request before saving any entries
        foreach (var entry in entries)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.ChangeId) ||
                string.IsNullOrWhiteSpace(entry.CommentId) ||
                string.IsNullOrWhiteSpace(entry.Reason) ||
                string.IsNullOrWhiteSpace(entry.IncomingSnapshotJson) ||
                entry.Operation != "Resolve" ||
                entry.Outcome != "Resolved" ||
                entry.RecordedAtUtc == default ||
                entry.BaseVersion < 0 ||
                entry.ServerVersionBefore < 0 ||
                entry.ServerVersionAfter != entry.ServerVersionBefore)
            {
                return BadRequest("Invalid resolution audit entry.");
            }

            if (!ids.Add(entry.Id))
            {
                return BadRequest("Duplicate audit IDs in this request.");
            }
        }

        var connection = await database.GetConnectionAsync();
        bool conflictingId = false;

        await connection.RunInTransactionAsync(transaction =>
        {
            var newEntries = new List<CommentAuditEntry>();

            // Check every existing ID before inserting anything
            foreach (var entry in entries)
            {
                var existing = transaction.Find<CommentAuditEntry>(entry.Id);

                if (existing is null)
                {
                    newEntries.Add(entry);
                }
                else if (!SameAuditEntry(existing, entry))
                {
                    conflictingId = true;
                    return;
                }
            }

            // Repeated upload keeps original
            foreach (var entry in newEntries)
            {
                transaction.Insert(entry);
            }
        });

        if (conflictingId)
        {
            return Conflict("An audit ID was already used.");
        }

        return Ok(entries.Select(entry => entry.Id).ToList());
    }

    private static bool SameAuditEntry(CommentAuditEntry existing, CommentAuditEntry incoming)
    {
        return existing.Id ==  incoming.Id &&
            existing.ChangeId == incoming.ChangeId &&
            existing.CommentId == incoming.CommentId &&
            existing.Operation == incoming.Operation &&
            existing.Outcome == incoming.Outcome &&
            existing.Reason == incoming.Reason &&
            existing.BaseVersion == incoming.BaseVersion &&
            existing.ServerVersionBefore == incoming.ServerVersionBefore &&
            existing.ServerVersionAfter == incoming.ServerVersionAfter &&
            existing.IncomingSnapshotJson == incoming.IncomingSnapshotJson &&
            existing.ServerSnapshotBeforeJson == incoming.ServerSnapshotBeforeJson &&
            existing.RecordedAtUtc == incoming.RecordedAtUtc;
    }
}