namespace BookClubApp.Api.Models;

public class PullChangesResponse
{
    public DateTime SyncTimeUtc { get; set; }

    public List<PageComment> Comments { get; set; } = new();
}