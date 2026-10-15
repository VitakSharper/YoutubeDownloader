using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class LinkActionTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string ListId = "PLx";

    [Fact]
    public void Video_FetchesWithoutHint()
    {
        var action = LinkAction.For(new YouTubeLink.Video(VideoId));

        Assert.Equal(new LinkAction.FetchVideo(VideoId, null), action);
    }

    [Theory]
    [InlineData(ListKind.Playlist)]
    [InlineData(ListKind.Album)]
    public void VideoInDownloadableList_OffersChoice(ListKind kind)
    {
        var action = LinkAction.For(new YouTubeLink.VideoInPlaylist(VideoId, ListId, kind));

        Assert.Equal(new LinkAction.OfferChoice(VideoId, ListId, kind), action);
    }

    [Fact]
    public void VideoInMix_FetchesVideoWithMixHint()
    {
        var action = LinkAction.For(new YouTubeLink.VideoInPlaylist(VideoId, "RD" + VideoId, ListKind.Mix));

        Assert.Equal(new LinkAction.FetchVideo(VideoId, LinkAction.MixHint), action);
    }

    [Fact]
    public void VideoInPrivateList_FetchesVideoWithPrivateListHint()
    {
        var action = LinkAction.For(new YouTubeLink.VideoInPlaylist(VideoId, "WL", ListKind.PrivateToAccount));

        Assert.Equal(
            new LinkAction.FetchVideo(VideoId, "That playlist is private to your YouTube account — downloading just the video."),
            action);
    }

    [Theory]
    [InlineData(ListKind.Playlist)]
    [InlineData(ListKind.Album)]
    public void DownloadablePlaylist_OpensIt(ListKind kind)
    {
        var action = LinkAction.For(new YouTubeLink.Playlist(ListId, kind));

        Assert.Equal(new LinkAction.OpenPlaylist(ListId, kind), action);
    }

    [Theory]
    [InlineData(ListKind.Mix, LinkAction.MixPlaylistMessage)]
    [InlineData(ListKind.PrivateToAccount, LinkAction.PrivatePlaylistMessage)]
    public void MixOrPrivatePlaylist_IsRejected(ListKind kind, string expectedMessage)
    {
        var action = LinkAction.For(new YouTubeLink.Playlist(ListId, kind));

        Assert.Equal(new LinkAction.Reject(expectedMessage), action);
    }

    [Fact]
    public void Invalid_IsRejectedWithTodaysMessage()
    {
        var action = LinkAction.For(new YouTubeLink.Invalid());

        Assert.Equal(new LinkAction.Reject("That doesn't look like a valid YouTube URL."), action);
    }
}
