using BookClubApp.App.Models;
using BookClubApp.App.Services;
using Microsoft.Maui.ApplicationModel;

namespace BookClubApp.App;

public partial class MainPage : ContentPage
{
    private readonly CommentDatabase database;
    private bool busy;
    private readonly SemaphoreSlim operationLock = new(1, 1);

    private const string SampleBookId = "sample-book";
    private const int SamplePageNumber = 1;
    private readonly PushSyncService pushSyncService;
    private readonly PullSyncService pullSyncService;
    private CancellationTokenSource? syncCancellation;

    public MainPage(CommentDatabase database, PushSyncService pushSyncService, PullSyncService pullSyncService)
    {
        InitializeComponent();
        this.database = database;
        this.pushSyncService = pushSyncService;
        this.pullSyncService = pullSyncService;
    }

    // Read saved comments whenever the page opens.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunOperationAsync(LoadCommentsAsync);
        
        #if ANDROID
        // Request local network permission on Android API 37 and later
        if (OperatingSystem.IsAndroidVersionAtLeast(37))
        {
            try
            {
                var status = await Permissions.RequestAsync<LocalNetworkPermission>();
                
                if (status != PermissionStatus.Granted)
                {
                    StatusLabel.Text = "Local network permission is needed to sync with the server.";
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Could not request local network permission: {ex.Message}";
                return;
            }
        }
        #endif

            if (syncCancellation is null)
            {
                syncCancellation = new CancellationTokenSource();
                _ = RunAutomaticSyncAsync(syncCancellation.Token);
            }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (busy)
            return;

        string text = CommentEditor.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusLabel.Text = "Write a comment before saving.";
            return;
        }

        await RunOperationAsync(async () =>
        {
            DateTime now = DateTime.UtcNow;

            var comment = new PageComment
            {
                BookId = SampleBookId,
                PageNumber = SamplePageNumber,
                AuthorId = "local-demo-user",
                AuthorName = "Local reader",
                CommentText = text,
                CreatedAt = now,
                UpdatedAt = now
            };

            await database.AddCommentAsync(comment);

            CommentEditor.Text = string.Empty;
            StatusLabel.Text = "Comment saved on this device.";

            // Saving and refreshing are separate operations.
            try
            {
                await LoadCommentsAsync();
            }
            catch (Exception ex)
            {
                StatusLabel.Text =
                    $"Comment saved, but reloading failed: {ex.Message}";
            }
        });
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await RunOperationAsync(LoadCommentsAsync);
    }

    private async void OnDownloadClicked(object? sender, EventArgs e)
    {
        await RunOperationAsync(async () =>
        {
            int downloadedCount = await pullSyncService.DownloadChangesAsync();
            StatusLabel.Text = $"Downloaded comments: {downloadedCount}. Tap Apply downloaded changes";
        });
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        await RunOperationAsync(async () =>
        {
            var state = await database.GetPullSyncStateAsync();

            if (state.PendingSyncTimeUtc is null)
            {
                StatusLabel.Text = "Download changes before applying. No changes were applied.";
                return;
            }

            int count = await pullSyncService.ApplyChangesAsync();

            try
            {
                await LoadCommentsAsync();
                StatusLabel.Text = $"Downloaded comments applied: {count}.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text =
                    $"Downloaded comments applied, but reloading failed: {ex.Message}";
            }
            
        });
    }

    private async Task LoadCommentsAsync()
    {
        var comments = await database.GetCommentsForPageAsync(
            SampleBookId, SamplePageNumber);

        BindableLayout.SetItemsSource(CommentsLayout, comments);

        await LoadConflictsAsync();

        StatusLabel.Text = comments.Count == 0
            ? "No saved comments on this page yet."
            : $"Comments saved on this device: {comments.Count}";
    }
	private async void OnEditClicked(object? sender, EventArgs e)
{
    if (sender is not Button button ||
        button.BindingContext is not PageComment comment)
        return;

    await RunOperationAsync(async () =>
    {
        string? text = await DisplayPromptAsync(
            "Edit comment",
            "Update your comment:",
            accept: "Save",
            cancel: "Cancel",
            initialValue: comment.CommentText);

        if (text is null)
            return;

        if (string.IsNullOrWhiteSpace(text))
        {
            StatusLabel.Text = "Comment text cannot be empty.";
            return;
        }

        if (text.Trim() == comment.CommentText)
        {
            StatusLabel.Text = "No changes were made.";
            return;
        }

        await database.UpdateCommentAsync(comment.Id, text);

        try
        {
            await LoadCommentsAsync();
            StatusLabel.Text = "Comment updated and change recorded.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text =
                $"Comment updated, but reloading failed: {ex.Message}";
        }
    });
}

private async void OnDeleteClicked(object? sender, EventArgs e)
{
    if (sender is not Button button ||
        button.BindingContext is not PageComment comment)
        return;

    await RunOperationAsync(async () =>
    {
        bool confirmed = await DisplayAlertAsync(
            "Delete comment?",
            "Remove this comment from the page?",
            "Delete",
            "Cancel");

        if (!confirmed)
            return;

        await database.DeleteCommentAsync(comment.Id);

        try
        {
            await LoadCommentsAsync();
            StatusLabel.Text = "Comment deleted and change recorded.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text =
                $"Comment deleted, but reloading failed: {ex.Message}";
        }
    });
}

    // Prevent overlapping operations and display any errors.
    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (busy)
            return;

        busy = true;
        bool lockTaken = false;

        try
        {
            SaveButton.IsEnabled = false;
            ReloadButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            SyncButton.IsEnabled = false;
            ConflictsLayout.IsEnabled = false;
            CommentsLayout.IsEnabled = false;
            CommentEditor.IsEnabled = false;

            await operationLock.WaitAsync();
            lockTaken = true;
            await operation();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Could not complete operation: {ex.Message}";
        }
        finally
        {
            if (lockTaken)
                operationLock.Release();

            busy = false;
            SaveButton.IsEnabled = true;
            ReloadButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            ApplyButton.IsEnabled = true;
            SyncButton.IsEnabled = true;
            ConflictsLayout.IsEnabled = true;
            CommentsLayout.IsEnabled = true;
            CommentEditor.IsEnabled = true;
        }
    }

    private async Task RunAutomaticSyncAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (busy)
                    continue;

                bool lockTaken = await operationLock.WaitAsync(0, token);

                if (!lockTaken)
                    continue;

                try
                {
                    int sent = await pushSyncService.PushChangesAsync(token);
                    token.ThrowIfCancellationRequested();

                    if (!busy)
                    {
                        await LoadConflictsAsync();

                        if (sent > 0)
                            StatusLabel.Text = $"Automatically synced changes: {sent}.";
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Synchronization stopped when leaving the page.
                    return;
                }
                catch (Exception ex)
                {
                    if (!busy && !token.IsCancellationRequested)
                        StatusLabel.Text = $"Automatic sync failed: {ex.Message}";
                }
                finally
                {
                    operationLock.Release();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Synchronization stopped when leaving the page.
        }
    }

    protected override void OnDisappearing()
    {
        syncCancellation?.Cancel();
        syncCancellation?.Dispose();
        syncCancellation = null;

        base.OnDisappearing();
    }
}