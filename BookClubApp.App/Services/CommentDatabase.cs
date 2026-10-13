using BookClubApp.App.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace BookClubApp.App.Services;

public class CommentDatabase
{
    private readonly Lazy<Task<SQLiteAsyncConnection>> database =
        new(CreateDatabaseAsync);

    // Open the database and create the table if needed.
    private static async Task<SQLiteAsyncConnection> CreateDatabaseAsync()
    {
        string path = Path.Combine(
            FileSystem.AppDataDirectory,
            "bookclub.db3");

        var connection = new SQLiteAsyncConnection(path);

        await connection.CreateTableAsync<PageComment>();

        return connection;
    }

    // Save a new comment as its own record.
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

        var connection = await database.Value;

        return await connection.InsertAsync(comment);
    }

    // Load comments for one book page.
    public async Task<List<PageComment>> GetCommentsForPageAsync(
        string bookId, int pageNumber)
    {
        var connection = await database.Value;

        return await connection.Table<PageComment>()
            .Where(comment =>
                comment.BookId == bookId &&
                comment.PageNumber == pageNumber)
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync();
    }

    // Load all saved comments.
    public async Task<List<PageComment>> GetAllCommentsAsync()
    {
        var connection = await database.Value;

        return await connection.Table<PageComment>()
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync();
    }
}