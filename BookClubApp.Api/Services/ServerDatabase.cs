using BookClubApp.Api.Models;
using SQLite;

namespace BookClubApp.Api.Services;

public class ServerDatabase
{
    private readonly Lazy<Task<SQLiteAsyncConnection>> database;

    public ServerDatabase(IWebHostEnvironment environment)
    {
        string folder = Path.Combine(
            environment.ContentRootPath, "App_Data");

        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "bookclub-server.db3");

        database = new Lazy<Task<SQLiteAsyncConnection>>(
            () => CreateDatabaseAsync(path));
    }

    private static async Task<SQLiteAsyncConnection> CreateDatabaseAsync(
        string path)
    {
        var connection = new SQLiteAsyncConnection(path);

        await connection.CreateTableAsync<PageComment>();
        await connection.CreateTableAsync<ReceivedCommentChange>();
        await connection.CreateTableAsync<CommentAuditEntry>();

        // Existing server comments start at version zero.
        // Future accepted changes will advance their versions.
        await connection.ExecuteAsync(
            "UPDATE PageComment SET Version = 0 WHERE Version IS NULL");

        return connection;
    }

    public async Task InitializeAsync()
    {
        await database.Value;
    }

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        return await database.Value;
    }
}