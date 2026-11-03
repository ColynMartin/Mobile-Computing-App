using BookClubApp.App.Models;
using Microsoft.Maui.Storage;
using SQLite;
using System.Text.Json;

namespace BookClubApp.App.Services;

public class CommentDatabase
{
    private readonly Lazy<Task<SQLiteAsyncConnection>> database =
        new(CreateDatabaseAsync);

    private static async Task<SQLiteAsyncConnection> CreateDatabaseAsync()
    {
        string path = Path.Combine(
            FileSystem.AppDataDirectory,
            "bookclub.db3");

        var connection = new SQLiteAsyncConnection(path);

        await connection.CreateTableAsync<PageComment>();
        await connection.CreateTableAsync<CommentChange>();
        await connection.CreateTableAsync<IncomingComment>();
        await connection.CreateTableAsync<PullSyncState>();

        // Keep older comments visible after adding IsDeleted.
        await connection.ExecuteAsync(
            "UPDATE PageComment SET IsDeleted = 0 WHERE IsDeleted IS NULL");

        return connection;
    }

    // Save the new comment and its change record together.
    public async Task<int> AddCommentAsync(PageComment comment)
    {
        if (string.IsNullOrWhiteSpace(comment.Id))
            throw new ArgumentException("A comment ID is required.");

        if (string.IsNullOrWhiteSpace(comment.BookId))
            throw new ArgumentException("A book ID is required.");

        if (comment.PageNumber < 1)
            throw new ArgumentException("Page number must be at least 1.");

        if (string.IsNullOrWhiteSpace(comment.CommentText))
            throw new ArgumentException("Comment text cannot be empty.");

        comment.CommentText = comment.CommentText.Trim();
        comment.IsDeleted = false;
        comment.CreatedAt = DateTime.UtcNow;
        comment.UpdatedAt = comment.CreatedAt;

        var connection = await database.Value;
        int rows = 0;

        await connection.RunInTransactionAsync(transaction =>
        {
            rows = transaction.Insert(comment);
            RecordChange(transaction, comment, "Create");
        });

        return rows;
    }

    // Edit an existing comment without changing its ID.
    public async Task UpdateCommentAsync(string commentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Comment text cannot be empty.");

        var connection = await database.Value;

        await connection.RunInTransactionAsync(transaction =>
        {
            var comment = GetActiveComment(transaction, commentId);

            comment.CommentText = text.Trim();
            comment.UpdatedAt = DateTime.UtcNow;

            transaction.Update(comment);
            RecordChange(transaction, comment, "Update");
        });
    }

    // Hide the comment but keep its deletion record.
    public async Task DeleteCommentAsync(string commentId)
    {
        var connection = await database.Value;

        await connection.RunInTransactionAsync(transaction =>
        {
            var comment = GetActiveComment(transaction, commentId);

            comment.IsDeleted = true;
            comment.UpdatedAt = DateTime.UtcNow;

            transaction.Update(comment);
            RecordChange(transaction, comment, "Delete");
        });
    }

    // Show only active comments for this page.
    public async Task<List<PageComment>> GetCommentsForPageAsync(
        string bookId, int pageNumber)
    {
        var connection = await database.Value;

        return await connection.Table<PageComment>()
            .Where(comment =>
                comment.BookId == bookId &&
                comment.PageNumber == pageNumber &&
                comment.IsDeleted == false)
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync();
    }

    // Include deleted records when inspecting the database.
    public async Task<List<PageComment>> GetAllCommentsAsync()
    {
        var connection = await database.Value;

        return await connection.Table<PageComment>()
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync();
    }

    // Read the stored change history.
    public async Task<List<CommentChange>> GetChangesAsync()
    {
        var connection = await database.Value;

        return await connection.Table<CommentChange>()
            .OrderBy(change => change.ChangedAt)
            .ToListAsync();
    }

    // Mark changes as synced after the server confirms them.
    public async Task MarkChangesSyncedAsync(List<string> changeIds)
    {
        var connection = await database.Value;

        foreach (var id in changeIds)
        {
            var change = await connection.Table<CommentChange>()
                .Where(change => change.Id == id)
                .FirstOrDefaultAsync();

            if (change is not null)
            {
                change.IsSynced = true;
                await connection.UpdateAsync(change);
            }
        }
    }

    // Read the saved pull checkpoint and any pending batch.
    public async Task<PullSyncState> GetPullSyncStateAsync()
    {
        var connection = await database.Value;

        return await connection.FindAsync<PullSyncState>("pull")
            ?? new PullSyncState();
    }

    // Store downloaded comments before applying them.
    public async Task SaveIncomingCommentsAsync(
        List<PageComment> comments,
        DateTime syncTimeUtc)
    {
        if (syncTimeUtc == default)
            throw new ArgumentException("A server sync time is required.");

        if (comments.Any(comment =>
            string.IsNullOrWhiteSpace(comment.Id)))
        {
            throw new ArgumentException(
                "Every downloaded comment must have an ID.");
        }

        if (comments.Select(comment => comment.Id).Distinct().Count()
            != comments.Count)
        {
            throw new ArgumentException(
                "The downloaded batch contains duplicate comment IDs.");
        }

        var connection = await database.Value;

        await connection.RunInTransactionAsync(transaction =>
        {
            var state = transaction.Find<PullSyncState>("pull")
                ?? new PullSyncState();

            if (state.PendingSyncTimeUtc is not null)
            {
                throw new InvalidOperationException(
                    "Process the downloaded batch before downloading another.");
            }

            if (state.LastAppliedSyncTimeUtc is DateTime lastSync &&
                syncTimeUtc < lastSync)
            {
                throw new InvalidOperationException(
                    "The server returned an older sync checkpoint.");
            }

            DateTime downloadedAt = DateTime.UtcNow;

            foreach (var comment in comments)
            {
                bool hasPendingChanges = transaction.Table<CommentChange>()
                    .Where(change =>
                        change.CommentId == comment.Id &&
                        change.IsSynced == false)
                    .Count() > 0;

                if (hasPendingChanges)
                {
                    throw new InvalidOperationException(
                        "A downloaded comment has pending local changes. " +
                        "Sync those changes before downloading again.");
                }

                var localComment =
                    transaction.Find<PageComment>(comment.Id);

                transaction.Insert(new IncomingComment
                {
                    CommentId = comment.Id,
                    SnapshotJson = JsonSerializer.Serialize(comment),
                    LocalSnapshotJson = localComment is null
                        ? null
                        : JsonSerializer.Serialize(localComment),
                    DownloadedAtUtc = downloadedAt,
                    SyncTimeUtc = syncTimeUtc
                });
            }

            state.PendingSyncTimeUtc = syncTimeUtc;
            transaction.InsertOrReplace(state);
        });
    }

    // Apply the downloaded batch and its checkpoint together.
    public async Task<int> ApplyIncomingCommentsAsync()
    {
        var connection = await database.Value;
        int appliedCount = 0;

        await connection.RunInTransactionAsync(transaction =>
        {
            var state = transaction.Find<PullSyncState>("pull");

            if (state?.PendingSyncTimeUtc is null)
                return;

            var incoming = transaction.Table<IncomingComment>().ToList();

            foreach (var item in incoming)
            {
                var comment = JsonSerializer.Deserialize<PageComment>(
                    item.SnapshotJson);

                if (comment is null || comment.Id != item.CommentId)
                {
                    throw new InvalidOperationException(
                        "A downloaded comment is invalid.");
                }

                var localComment =
                    transaction.Find<PageComment>(item.CommentId);

                string? currentSnapshot = localComment is null
                    ? null
                    : JsonSerializer.Serialize(localComment);

                bool hasPendingChanges = transaction.Table<CommentChange>()
                    .Where(change =>
                        change.CommentId == item.CommentId &&
                        change.IsSynced == false)
                    .Count() > 0;

                if (hasPendingChanges ||
                    currentSnapshot != item.LocalSnapshotJson)
                {
                    throw new InvalidOperationException(
                        "A local comment changed after downloading. " +
                        "The batch was not applied; both versions are retained.");
                }

                if (localComment is null)
                    transaction.Insert(comment);
                else
                    transaction.Update(comment);

                appliedCount++;
            }

            state.LastAppliedSyncTimeUtc = state.PendingSyncTimeUtc;
            state.PendingSyncTimeUtc = null;

            transaction.Update(state);
            transaction.DeleteAll<IncomingComment>();
        });

        return appliedCount;
    }

    private static PageComment GetActiveComment(
        SQLiteConnection connection, string commentId)
    {
        var comment = connection.Find<PageComment>(commentId);

        if (comment is null || comment.IsDeleted)
        {
            throw new InvalidOperationException(
                "This comment no longer exists or has been deleted.");
        }

        return comment;
    }

    private static void RecordChange(
        SQLiteConnection connection,
        PageComment comment,
        string operation)
    {
        var change = new CommentChange
        {
            CommentId = comment.Id,
            Operation = operation,
            ChangedAt = comment.UpdatedAt,
            SnapshotJson = JsonSerializer.Serialize(comment),
            IsSynced = false
        };

        connection.Insert(change);
    }
}