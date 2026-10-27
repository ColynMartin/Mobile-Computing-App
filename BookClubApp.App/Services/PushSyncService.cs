using BookClubApp.App.Services;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookClubApp.App.Services;

public class PushSyncService
{
    private readonly CommentDatabase database;
    private readonly HttpClient client = new();

    public PushSyncService(CommentDatabase database)
    {
        this.database = database;
    }

    public async Task<int> PushChangesAsync()
    {
        var changes = await database.GetChangesAsync();
        var pending = changes.Where(change => !change.IsSynced).ToList();

        if (pending.Count == 0)
            return 0;

        var response = await client.PostAsJsonAsync(
            "http://localhost:5240/pushChanges", pending);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PushResult>();

        if (result is null)
            throw new InvalidOperationException("No confirmation received.");

        await database.MarkChangesSyncedAsync(result.ProcessedChangeIds);

        return result.ProcessedChangeIds.Count;
    }

    public async Task StartAutomaticSyncAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PushChangesAsync();
            }
            catch (HttpRequestException)
            {
                // Server unavailable. Try again later.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Request timed out. Try again later.
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private class PushResult
    {
        public List<string> ProcessedChangeIds { get; set; } = new();
    }
}