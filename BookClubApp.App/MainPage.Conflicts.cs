using BookClubApp.App.Models;
using System.Text.Json;

namespace BookClubApp.App;

public partial class MainPage
{
    private async Task LoadConflictsAsync()
    {
        var conflicts = await database.GetUnresolvedConflictsAsync();
        var comments = (await database.GetAllCommentsAsync())
            .ToDictionary(comment => comment.Id);

        ConflictsLayout.Children.Clear();

        // A comment can have several rejected edits. Show one decision for its latest text.
        var reviews = conflicts.GroupBy(conflict => conflict.CommentId)
            .Select(group => group.OrderByDescending(conflict => conflict.ServerVersion)
                .ThenByDescending(conflict => conflict.DetectedAtUtc).First())
            .ToList();

        ConflictStatusLabel.Text = reviews.Count == 0
            ? "No unresolved conflicts."
            : $"Comments needing review: {reviews.Count}. Choose which version to keep.";

        foreach (var conflict in reviews)
        {
            comments.TryGetValue(conflict.CommentId, out var local);
            var server = conflict.ServerSnapshotJson is null ? null
                : JsonSerializer.Deserialize<PageComment>(conflict.ServerSnapshotJson);

            string? localSnapshot = local is null ? null : JsonSerializer.Serialize(local);
            var content = new VerticalStackLayout { Spacing = 8 };
            content.Children.Add(new Label
            {
                Text = "Comment conflict",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold
            });
            content.Children.Add(new Label { Text = conflict.Reason });
            content.Children.Add(new Label { Text = "My device", FontAttributes = FontAttributes.Bold });
            content.Children.Add(new Label { Text = DescribeVersion(local) });
            content.Children.Add(new Label { Text = "Server", FontAttributes = FontAttributes.Bold });
            content.Children.Add(new Label { Text = DescribeVersion(server) });

            bool canKeepLocal = local is not null &&
                !(server?.IsDeleted == true && !local.IsDeleted);

            var keepLocal = new Button { Text = "Keep my version", IsEnabled = canKeepLocal };
            keepLocal.Clicked += async (_, _) =>
                await ChooseConflictVersionAsync(conflict, true, localSnapshot);
            content.Children.Add(keepLocal);

            if (!canKeepLocal && local is not null && !local.IsDeleted)
            {
                content.Children.Add(new Label
                {
                    Text = "This comment was deleted on the server. Copy your text before keeping the server version if you want to save it as a new comment."
                });
                var copyText = new Button { Text = "Copy my text to editor" };
                copyText.Clicked += (_, _) => CommentEditor.Text = local.CommentText;
                content.Children.Add(copyText);
            }

            var keepServer = new Button { Text = "Keep server version" };
            keepServer.Clicked += async (_, _) =>
                await ChooseConflictVersionAsync(conflict, false, localSnapshot);
            content.Children.Add(keepServer);

            ConflictsLayout.Children.Add(new Border
            {
                Padding = 12,
                StrokeThickness = 2,
                Content = content
            });
        }
    }

    private static string DescribeVersion(PageComment? comment)
    {
        if (comment is null)
            return "This version has no saved comment.";

        return comment.IsDeleted
            ? $"Deleted comment (version {comment.Version})\n{comment.CommentText}"
            : $"Version {comment.Version}\n{comment.CommentText}";
    }

    private async Task ChooseConflictVersionAsync(
        CommentConflict conflict, bool keepLocal, string? localSnapshot)
    {
        await RunOperationAsync(async () =>
        {
            bool confirmed = await DisplayAlertAsync(
                "Resolve comment conflict?",
                keepLocal
                    ? "Keep your device's version and queue it for sync? The server will check it again before accepting it."
                    : "Use the server version shown here? Your other edits will remain in the saved history.",
                keepLocal ? "Keep mine" : "Keep server",
                "Cancel");

            if (!confirmed)
                return;

            await pushSyncService.ResolveConflictAsync(
                conflict.ChangeId, keepLocal, localSnapshot);
            await LoadCommentsAsync();
            StatusLabel.Text = keepLocal
                ? "Device version kept. Tap Sync now to send any queued change."
                : "Server version kept. The decision was saved on this device.";
        });
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        await RunOperationAsync(async () =>
        {
            int sent = await pushSyncService.PushChangesAsync();
            var conflicts = await database.GetUnresolvedConflictsAsync();

            if (conflicts.Count > 0)
            {
                await LoadCommentsAsync();
                StatusLabel.Text = $"Sent changes: {sent}. Review the comment conflicts before downloading.";
                return;
            }

            var state = await database.GetPullSyncStateAsync();
            if (state.PendingSyncTimeUtc is not null)
                await pullSyncService.ApplyChangesAsync();

            await pullSyncService.DownloadChangesAsync();
            int applied = await pullSyncService.ApplyChangesAsync();
            await LoadCommentsAsync();
            StatusLabel.Text = $"Sync complete. Sent changes: {sent}. Applied comments: {applied}.";
        });
    }
}
