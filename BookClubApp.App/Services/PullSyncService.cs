using BookClubApp.App.Models;
using System.Globalization;
using System.Net.Http.Json;

namespace BookClubApp.App.Services;

public class PullSyncService
{
    private readonly CommentDatabase database;
    private readonly HttpClient client;
    private readonly SemaphoreSlim syncLock = new(1, 1);

    public PullSyncService(
        CommentDatabase database,
        HttpClient client)
    {
        this.database = database;
        this.client = client;
    }

    // Download and store a batch without changing visible comments yet.
    public async Task<int> DownloadChangesAsync()
    {
        await syncLock.WaitAsync();

        try
        {
            var state = await database.GetPullSyncStateAsync();

            if (state.PendingSyncTimeUtc is not null)
            {
                throw new InvalidOperationException(
                    "Apply the downloaded changes before downloading again.");
            }

            string route = "pullChanges";

            if (state.LastAppliedSyncTimeUtc is DateTime lastSync)
            {
                // SQLite stores the timestamp without its UTC kind.
                DateTime utc = DateTime.SpecifyKind(
                    lastSync, DateTimeKind.Utc);

                string since = Uri.EscapeDataString(
                    utc.ToString("O", CultureInfo.InvariantCulture));

                route += $"?since={since}";
            }

            var response =
                await client.GetFromJsonAsync<PullChangesResponse>(route);

            if (response is null ||
                response.SyncTimeUtc == default ||
                response.Comments is null)
            {
                throw new InvalidOperationException(
                    "The server returned an invalid pull response.");
            }

            await database.SaveIncomingCommentsAsync(
                response.Comments,
                response.SyncTimeUtc);

            return response.Comments.Count;
        }
        finally
        {
            syncLock.Release();
        }
    }

    // Apply the stored batch and save its checkpoint.
    public async Task<int> ApplyChangesAsync()
    {
        await syncLock.WaitAsync();

        try
        {
            return await database.ApplyIncomingCommentsAsync();
        }
        finally
        {
            syncLock.Release();
        }
    }
}