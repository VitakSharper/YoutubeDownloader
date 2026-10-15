using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class QueueSummaryTests
{
    [Fact]
    public void From_CountsEachKindOfState()
    {
        ItemState[] states =
        [
            new ItemState.Pending(),
            new ItemState.Fetching(Attempt: 1),
            new ItemState.Downloading(PipelineStage.DownloadingAudio, 0.5),
            new ItemState.Saved(@"C:\Music\001. Artist - Title.mp3", "MP3 · 320 kbps"),
            new ItemState.Saved(@"C:\Music\002. Artist - Other.mp3", "MP3 · 320 kbps"),
            new ItemState.Failed("Requires purchase", FailureKind.RequiresPurchase),
            new ItemState.Skipped("Already saved"),
            new ItemState.Cancelled()
        ];

        var summary = QueueSummary.From(states);

        Assert.Equal(new QueueSummary(Total: 8, Saved: 2, Failed: 1, Skipped: 1, Cancelled: 1, NotStarted: 3), summary);
    }

    [Fact]
    public void Describe_AllSaved()
    {
        var summary = new QueueSummary(Total: 3, Saved: 3, Failed: 0, Skipped: 0, Cancelled: 0, NotStarted: 0);

        Assert.Equal("3 of 3 saved", summary.Describe());
    }

    [Fact]
    public void Describe_WithFailuresAndSkips()
    {
        var summary = new QueueSummary(Total: 86, Saved: 84, Failed: 1, Skipped: 1, Cancelled: 0, NotStarted: 0);

        Assert.Equal("84 of 86 saved · 1 failed · 1 skipped", summary.Describe());
    }

    [Fact]
    public void Describe_WithCancelledAndNotStarted()
    {
        var summary = new QueueSummary(Total: 6, Saved: 1, Failed: 0, Skipped: 0, Cancelled: 2, NotStarted: 3);

        Assert.Equal("1 of 6 saved · 2 cancelled · 3 not started", summary.Describe());
    }
}
