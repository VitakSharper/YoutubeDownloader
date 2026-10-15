using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class ItemStateTests
{
    [Fact]
    public void IsFinal_SavedFailedSkippedCancelled_AreFinal()
    {
        ItemState[] states =
        [
            new ItemState.Saved(@"C:\Music\001. Artist - Title.mp3", "MP3 · 320 kbps"),
            new ItemState.Failed("Requires purchase", FailureKind.RequiresPurchase),
            new ItemState.Skipped("Removed"),
            new ItemState.Cancelled()
        ];

        Assert.All(states, state => Assert.True(state.IsFinal));
    }

    [Fact]
    public void IsFinal_PendingFetchingDownloading_AreNotFinal()
    {
        ItemState[] states =
        [
            new ItemState.Pending(),
            new ItemState.Fetching(Attempt: 1),
            new ItemState.Downloading(PipelineStage.Converting, 0.8)
        ];

        Assert.All(states, state => Assert.False(state.IsFinal));
    }
}
