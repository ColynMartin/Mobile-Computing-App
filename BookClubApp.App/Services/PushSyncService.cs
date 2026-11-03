using System.Net.Http;
using System.Net.Http.Json;

namespace BookClubApp.App.Services;

public class PushSyncService
{
    private readonly CommentDatabase database;
    private readonly HttpClient client;
    private readonly SemaphoreSlim pushLock = new(1, 1);

    public PushSyncService(
        CommentDatabase database,
        HttpClient client)
    {
        this.database = database;
        this.client = client;
    }

    public async Task<int> PushChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await pushLock.WaitAsync(cancellationToken);

        try
        {
            var changes = await database.GetChangesAsync();

            var pending = changes
                .Where(change => !change.IsSynced)
                .ToList();

            if (pending.Count == 0)
                return 0;

            using var response = await client.PostAsJsonAsync(
                "pushChanges",
                pending,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<PushResult>(
                    cancellationToken: cancellationToken);

            if (result?.ProcessedChangeIds is null)
            {
                throw new InvalidOperationException(
                    "No confirmation received.");
            }

            // Only acknowledge changes included in this request.
            var sentIds = pending
                .Select(change => change.Id)
                .ToHashSet();

            var confirmedIds = result.ProcessedChangeIds
                .Where(id => sentIds.Contains(id))
                .Distinct()
                .ToList();

            await database.MarkChangesSyncedAsync(confirmedIds);

            return confirmedIds.Count;
        }
        finally
        {
            pushLock.Release();
        }
    }

    public async Task StartAutomaticSyncAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PushChangesAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Server unavailable. Try again later.
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                // Request timed out. Try again later.
            }

            await Task.Delay(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
    }

    private class PushResult
    {
        public List<string> ProcessedChangeIds { get; set; } = new();
    }
}