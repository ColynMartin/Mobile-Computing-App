using BookClubApp.App.Models;
using BookClubApp.App.Services;

namespace BookClubApp.App;

public partial class MainPage : ContentPage
{
    private readonly CommentDatabase database;
    private bool busy;

    private const string SampleBookId = "sample-book";
    private const int SamplePageNumber = 1;

    public MainPage(CommentDatabase database)
    {
        InitializeComponent();
        this.database = database;
    }

    // Read saved comments whenever the page opens.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunOperationAsync(LoadCommentsAsync);
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

    private async Task LoadCommentsAsync()
    {
        var comments = await database.GetCommentsForPageAsync(
            SampleBookId, SamplePageNumber);

        BindableLayout.SetItemsSource(CommentsLayout, comments);

        StatusLabel.Text = comments.Count == 0
            ? "No saved comments on this page yet."
            : $"Comments saved on this device: {comments.Count}";
    }

    // Prevent overlapping operations and display any errors.
    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (busy)
            return;

        busy = true;
        SaveButton.IsEnabled = false;
        ReloadButton.IsEnabled = false;
        CommentEditor.IsEnabled = false;

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Could not complete operation: {ex.Message}";
        }
        finally
        {
            busy = false;
            SaveButton.IsEnabled = true;
            ReloadButton.IsEnabled = true;
            CommentEditor.IsEnabled = true;
        }
    }
}