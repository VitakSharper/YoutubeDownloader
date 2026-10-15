using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class ItemStateTextTests
{
    [Fact]
    public void Pending_IsBlank()
    {
        Assert.Equal("", ItemStateText.For(new ItemState.Pending()));
    }

    [Fact]
    public void Fetching_ShowsTheRetryAfterTheFirstAttempt()
    {
        Assert.Equal("Fetching…", ItemStateText.For(new ItemState.Fetching(Attempt: 1)));
        Assert.Equal("Fetching… (retry 1)", ItemStateText.For(new ItemState.Fetching(Attempt: 2)));
        Assert.Equal("Fetching… (retry 2)", ItemStateText.For(new ItemState.Fetching(Attempt: 3)));
    }

    [Fact]
    public void Downloading_NamesTheStage()
    {
        Assert.Equal("Downloading…", ItemStateText.For(new ItemState.Downloading(PipelineStage.DownloadingAudio, 0.2)));
        Assert.Equal("Downloading…", ItemStateText.For(new ItemState.Downloading(PipelineStage.DownloadingVideo, 0.2)));
        Assert.Equal("Converting…", ItemStateText.For(new ItemState.Downloading(PipelineStage.Converting, 0.8)));
        Assert.Equal("Saving…", ItemStateText.For(new ItemState.Downloading(PipelineStage.SavingOpus, 0.8)));
        Assert.Equal("Merging…", ItemStateText.For(new ItemState.Downloading(PipelineStage.Merging, 0.8)));
    }

    [Fact]
    public void FinalStates_ShowTheirText()
    {
        Assert.Equal("✓ Saved", ItemStateText.For(new ItemState.Saved(@"C:\Music\001. Artist - Title.mp3", "MP3 · 320 kbps")));
        Assert.Equal("Requires purchase", ItemStateText.For(new ItemState.Failed("Requires purchase", FailureKind.RequiresPurchase)));
        Assert.Equal("Already saved", ItemStateText.For(new ItemState.Skipped("Already saved")));
        Assert.Equal("Cancelled", ItemStateText.For(new ItemState.Cancelled()));
    }
}
