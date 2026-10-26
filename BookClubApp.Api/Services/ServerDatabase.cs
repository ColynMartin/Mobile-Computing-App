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

        return connection;
    }

    public async Task InitializeAsync()
    {
        await database.Value;
    }
}