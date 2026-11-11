using BookClubApp.App.Models;

namespace BookClubApp.App.Services;

public partial class CommentDatabase
{
    // Find device records that have not yet been synced to the server
    public async Task<List<CommentAuditEntry>> GetPendingAuditEntriesAsync()
    {
        var connection = await database.Value;
        return await connection.QueryAsync<CommentAuditEntry>(
            """
            SELECT audit.*
            FROM CommentAuditEntry AS audit
            WHERE audit.Operation = 'Resolve'
                AND audit.Outcome = 'Resolved'
                AND NOT EXISTS (
                    SELECT 1
                    FROM SyncedAuditEntry AS synced
                    WHERE synced.AuditEntryId = audit.Id
                )
            ORDER BY audit.RecordedAtUtc, audit.rowid
            """);
    }

    // Record successfully uploads without changing the audit records
    public async Task MarkAuditEntriesSyncedAsync(List<string> auditEntryIds)
    {
        var connection = await database.Value;

        await connection.RunInTransactionAsync(transaction =>
        {
            DateTime now = DateTime.UtcNow;

            foreach (string id in auditEntryIds.Distinct())
            {
                var audit = transaction.Find<CommentAuditEntry>(id);

                if (audit is null || audit.Operation != "Resolve" || audit.Outcome != "Resolved")
                {
                    throw new InvalidOperationException("Cannot mark an unknown resolution audit entry as synced.");
                }

                var existing = transaction.Find<SyncedAuditEntry>(id);

                if (existing is null)
                {
                    transaction.Insert(new SyncedAuditEntry
                    {
                        AuditEntryId = id,
                        SyncedAtUtc = now
                    });
                }
            }
        });
    }
}