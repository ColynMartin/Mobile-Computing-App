using BookClubApp.App.Models;
using System.Text.Json;

namespace BookClubApp.App.Services;

public partial class CommentDatabase
{
    // Store server evidence before acknowledging any local changes.
    public async Task SavePushEvidenceAsync(
        List<CommentChange> sentChanges,
        PushChangesResponse response)
    {
        if (response.Results is null || response.ProcessedChangeIds is null)
        {
            throw new InvalidOperationException(
                "The server did not return detailed sync results.");
        }

        var sentById = sentChanges.ToDictionary(change => change.Id);
        var returnedIds = new HashSet<string>();

        // Validate all results before saving anything.
        foreach (var result in response.Results)
        {
            if (!sentById.TryGetValue(result.ChangeId, out var sent) ||
                result.CommentId != sent.CommentId)
            {
                throw new InvalidOperationException(
                    "The server returned a result for an unexpected change.");
            }

            if (!returnedIds.Add(result.ChangeId))
            {
                throw new InvalidOperationException(
                    "The server returned duplicate sync results.");
            }

            if (result.Outcome != "Accepted" &&
                result.Outcome != "Conflict")
            {
                throw new InvalidOperationException(
                    "The server returned an unknown sync outcome.");
            }

            if (result.ServerComment is not null &&
                result.ServerComment.Id != sent.CommentId)
            {
                throw new InvalidOperationException(
                    "The returned server comment has the wrong ID.");
            }

            var audit = result.AuditEntry;

            if (audit is not null &&
                (string.IsNullOrWhiteSpace(audit.Id) ||
                 audit.ChangeId != sent.Id ||
                 audit.CommentId != sent.CommentId ||
                 audit.Outcome != result.Outcome))
            {
                throw new InvalidOperationException(
                    "The server returned an invalid audit record.");
            }
        }

        if (returnedIds.Count != sentById.Count)
        {
            throw new InvalidOperationException(
                "The server did not return an outcome for every change.");
        }

        var acceptedIds = response.Results
            .Where(result => result.Outcome == "Accepted")
            .Select(result => result.ChangeId)
            .ToHashSet();

        if (!acceptedIds.SetEquals(response.ProcessedChangeIds))
        {
            throw new InvalidOperationException(
                "The server's acknowledgments do not match its accepted results.");
        }

        var connection = await database.Value;

        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var result in response.Results)
            {
                var sent = sentById[result.ChangeId];

                // Preserve the original audit entry on repeated responses.
                if (result.AuditEntry is not null)
                {
                    var existingAudit =
                        transaction.Find<CommentAuditEntry>(
                            result.AuditEntry.Id);

                    if (existingAudit is null)
                    {
                        transaction.Insert(result.AuditEntry);
                    }
                }

                if (result.Outcome == "Accepted")
                {
                    var change = transaction.Find<CommentChange>(sent.Id);

                    if (change is not null)
                    {
                        change.IsSynced = true;
                        transaction.Update(change);
                    }

                    var local = transaction.Find<PageComment>(sent.CommentId);

                    // Preserve newer local text while remembering the server version.
                    if (local is not null && result.ServerComment is not null &&
                        result.ServerComment.Version > local.Version)
                    {
                        local.Version = result.ServerComment.Version;
                        transaction.Update(local);
                    }

                    continue;
                }

                var existingConflict =
                    transaction.Find<CommentConflict>(result.ChangeId);

                // Do not duplicate a conflict or reset its resolution.
                if (existingConflict is not null)
                    continue;

                transaction.Insert(new CommentConflict
                {
                    ChangeId = sent.Id,
                    CommentId = sent.CommentId,
                    Operation = sent.Operation,
                    Reason = result.Reason,
                    BaseVersion = sent.BaseVersion,
                    ServerVersion = result.ServerComment?.Version,
                    LocalSnapshotJson = sent.SnapshotJson,
                    ServerSnapshotJson = result.ServerComment is null
                        ? null
                        : JsonSerializer.Serialize(result.ServerComment),
                    DetectedAtUtc = DateTime.UtcNow,
                    IsResolved = false
                });
            }
        });
    }

    // Read unresolved conflicts for the app's resolution screen.
    public async Task<List<CommentConflict>> GetUnresolvedConflictsAsync()
    {
        var connection = await database.Value;

        return await connection.Table<CommentConflict>()
            .Where(conflict => conflict.IsResolved == false)
            .OrderBy(conflict => conflict.DetectedAtUtc)
            .ToListAsync();
    }

    // Read locally stored audit evidence.
    public async Task<List<CommentAuditEntry>> GetAuditEntriesAsync()
    {
        var connection = await database.Value;

        return await connection.Table<CommentAuditEntry>()
            .OrderBy(entry => entry.RecordedAtUtc)
            .ToListAsync();
    }
}
