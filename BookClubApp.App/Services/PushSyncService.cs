using BookClubApp.App.Models;
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
            var conflicts =
                await database.GetUnresolvedConflictsAsync();

            var blockedCommentIds = conflicts
                .Select(conflict => conflict.CommentId)
                .ToHashSet();

            var changes = await database.GetPendingChangesAsync();

            var pending = changes
                .Where(change =>
                    !blockedCommentIds.Contains(change.CommentId))
                .ToList();

            if (pending.Count == 0)
                return 0;

            using var response = await client.PostAsJsonAsync(
                "pushChanges",
                pending,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<PushChangesResponse>(
                    cancellationToken: cancellationToken);

            if (result is null ||
                result.ProcessedChangeIds is null ||
                result.Results is null)
            {
                throw new InvalidOperationException(
                    "The server did not return a valid sync response.");
            }

            // Save audit records and conflicts before acknowledging changes.
            await database.SavePushEvidenceAsync(pending, result);

            var sentIds = pending
                .Select(change => change.Id)
                .ToHashSet();

            var acceptedIds = result.Results
                .Where(item => item.Outcome == "Accepted")
                .Select(item => item.ChangeId)
                .ToHashSet();

            var confirmedIds = result.ProcessedChangeIds
                .Where(id =>
                    sentIds.Contains(id) &&
                    acceptedIds.Contains(id))
                .Distinct()
                .ToList();

            return confirmedIds.Count;
        }
        finally
        {
            pushLock.Release();
        }
    }

    public async Task ResolveConflictAsync(
        string changeId, bool keepLocal, string? expectedLocalSnapshot)
    {
        // A response already in flight must finish before resolving its conflict.
        await pushLock.WaitAsync();

        try
        {
            await database.ResolveConflictAsync(
                changeId, keepLocal, expectedLocalSnapshot);
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
                // Server unavailable. Pending changes stay on the device.
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
}
