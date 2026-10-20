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

    private static PageComment GetActiveComment(
        SQLiteConnection connection, string commentId)
    {
        var comment = connection.Find<PageComment>(commentId);

        if (comment is null || comment.IsDeleted)
            throw new InvalidOperationException(
                "This comment no longer exists or has been deleted.");

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