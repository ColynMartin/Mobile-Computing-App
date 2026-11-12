using BookClubApp.App.Models;
using System.Text.Json;

namespace BookClubApp.App.Services;

public partial class CommentDatabase
{
    public async Task ResolveConflictAsync(
        string changeId, bool keepLocal, string? expectedLocalSnapshot)
    {
        var connection = await database.Value;

        // Save the chosen version, queue changes, and record the decision together.
        await connection.RunInTransactionAsync(transaction =>
        {
            var conflict = transaction.Find<CommentConflict>(changeId);

            if (conflict is null || conflict.IsResolved)
                throw new InvalidOperationException("This conflict was already resolved. Reload the comments.");

            var local = transaction.Find<PageComment>(conflict.CommentId);
            string? currentSnapshot = local is null
                ? null
                : JsonSerializer.Serialize(local);

            if (currentSnapshot != expectedLocalSnapshot)
                throw new InvalidOperationException("This comment changed during review. Reload and review it again.");

            var server = conflict.ServerSnapshotJson is null
                ? null
                : JsonSerializer.Deserialize<PageComment>(conflict.ServerSnapshotJson);

            if (server is not null && server.Id != conflict.CommentId)
                throw new InvalidOperationException("The saved server version has an unexpected comment ID.");

            if (keepLocal && local is null)
                throw new InvalidOperationException("The device version is missing. Reload before choosing a version.");

            if (keepLocal && server?.IsDeleted == true && !local!.IsDeleted)
                throw new InvalidOperationException("The server version was deleted. Keep the server version, then save your text as a new comment.");

            var unresolved = transaction.Table<CommentConflict>()
                .Where(item => item.CommentId == conflict.CommentId && item.IsResolved == false)
                .ToList();

            // One decision covers all rejected edits in this comment's local chain.
            foreach (var change in transaction.Table<CommentChange>()
                .Where(item => item.CommentId == conflict.CommentId &&
                    item.IsSynced == false && item.IsSuperseded == false).ToList())
            {
                change.IsSuperseded = true;
                transaction.Update(change);
            }

            DateTime now = DateTime.UtcNow;
            string resolution = keepLocal ? "KeepLocal" : "KeepServer";

            if (keepLocal)
            {
                local!.Version = server?.Version ?? 0;
                local.UpdatedAt = now;
                transaction.Update(local);

                // If both sides already agree that it was deleted, no push is needed.
                if (!(local.IsDeleted && (server is null || server.IsDeleted)))
                {
                    string operation = "Update";
                    if (server is null)
                        operation = "Create";
                    else if (local.IsDeleted)
                        operation = "Delete";

                    RecordChange(transaction, local, operation);
                }
            }
            else if (server is not null)
            {
                transaction.InsertOrReplace(server);
            }
            else if (local is not null)
            {
                // Preserve a local deletion record when the server has no comment.
                local.IsDeleted = true;
                local.Version = 0;
                local.UpdatedAt = now;
                transaction.Update(local);
            }

            foreach (var item in unresolved)
            {
                item.IsResolved = true;
                item.Resolution = resolution;
                item.ResolvedAtUtc = now;
                transaction.Update(item);
            }

            // A staged pull used the old local snapshot. Download it again after review.
            var pullState = transaction.Find<PullSyncState>("pull");

            if (pullState?.PendingSyncTimeUtc is not null)
            {
                transaction.DeleteAll<IncomingComment>();
                pullState.PendingSyncTimeUtc = null;
                transaction.Update(pullState);
            }

            transaction.Insert(new CommentAuditEntry
            {
                ChangeId = conflict.ChangeId,
                CommentId = conflict.CommentId,
                Operation = "Resolve",
                Outcome = "Resolved",
                Reason = keepLocal ? "Kept the device version." : "Kept the server version.",
                BaseVersion = conflict.BaseVersion,
                ServerVersionBefore = server?.Version,
                ServerVersionAfter = server?.Version,
                IncomingSnapshotJson = currentSnapshot ?? conflict.LocalSnapshotJson,
                ServerSnapshotBeforeJson = conflict.ServerSnapshotJson,
                RecordedAtUtc = now
            });
        });
    }
}
