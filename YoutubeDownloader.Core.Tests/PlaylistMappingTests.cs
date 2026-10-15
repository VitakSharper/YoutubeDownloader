using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class PlaylistMappingTests
{
    private const int MinWidth = 240;
    private const string ListId = "PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e";
    private const string VideoId = "uPZSSdkGQhM";
    private const string ChannelId = "UCuAXFkgsw1L7xaCfnd5JJOw";

    private static readonly Author Channel = new(ChannelId, "Some Channel");

    private static PlaylistVideo Video(TimeSpan? duration = null, IReadOnlyList<Thumbnail>? thumbnails = null)
        => new(ListId, VideoId, "Some Title", Channel, duration, thumbnails ?? []);

    private static Thumbnail Thumb(string url, int width, int height) => new(url, new Resolution(width, height));

    [Fact]
    public void ToQueueItem_UsesWatchUrlWithoutList()
    {
        var source = Video(TimeSpan.FromMinutes(3));
        Assert.Contains("list=", source.Url);   // what YoutubeExplode gives, and what must not be stored

        var item = PlaylistMapping.ToQueueItem(source, index: 1, MinWidth);

        Assert.Equal("https://www.youtube.com/watch?v=uPZSSdkGQhM", item.Url);
        Assert.DoesNotContain("list=", item.Url);
    }

    [Fact]
    public void ToQueueItem_CopiesIdTitleChannelDurationAndIndex()
    {
        var item = PlaylistMapping.ToQueueItem(Video(TimeSpan.FromSeconds(215)), index: 7, MinWidth);

        Assert.Equal(VideoId, item.VideoId);
        Assert.Equal("Some Title", item.Title);
        Assert.Equal("Some Channel", item.Author);
        Assert.Equal(TimeSpan.FromSeconds(215), item.Duration);
        Assert.Equal(7, item.Index);
    }

    [Fact]
    public void ToQueueItem_NullDuration_StaysNull()
    {
        var item = PlaylistMapping.ToQueueItem(Video(duration: null), index: 1, MinWidth);

        Assert.Null(item.Duration);
    }

    [Fact]
    public void ToQueueItem_PicksSmallestThumbnailAtLeastMinWidth()
    {
        var thumbnails = new[]
        {
            Thumb("https://i.ytimg.com/vi/x/default.jpg", 120, 90),
            Thumb("https://i.ytimg.com/vi/x/mqdefault.jpg", 320, 180),
            Thumb("https://i.ytimg.com/vi/x/maxresdefault.jpg", 1280, 720),
        };

        var item = PlaylistMapping.ToQueueItem(Video(thumbnails: thumbnails), index: 1, MinWidth);

        Assert.Equal("https://i.ytimg.com/vi/x/mqdefault.jpg", item.ThumbnailUrl);
    }

    [Fact]
    public void ToQueueItem_NoThumbnails_HasNullThumbnail()
    {
        var item = PlaylistMapping.ToQueueItem(Video(thumbnails: []), index: 1, MinWidth);

        Assert.Null(item.ThumbnailUrl);
    }

    [Fact]
    public void ToPlaylistInfo_CopiesIdTitleAuthorCountAndThumbnail()
    {
        var playlist = new Playlist(ListId, "Some List", Channel, "A description", 50,
            [Thumb("https://i.ytimg.com/vi/x/hqdefault.jpg", 480, 360)]);

        var info = PlaylistMapping.ToPlaylistInfo(playlist, MinWidth);

        Assert.Equal(ListId, info.ListId);
        Assert.Equal("Some List", info.Title);
        Assert.Equal("Some Channel", info.Author);
        Assert.Equal(50, info.Count);
        Assert.Equal("https://i.ytimg.com/vi/x/hqdefault.jpg", info.ThumbnailUrl);
    }

    [Fact]
    public void ToPlaylistInfo_MixOrAlbum_HasNullAuthorAndCount()
    {
        var playlist = new Playlist("RDuPZSSdkGQhM", "Mix - Some Title", author: null, "", count: null, []);

        var info = PlaylistMapping.ToPlaylistInfo(playlist, MinWidth);

        Assert.Null(info.Author);
        Assert.Null(info.Count);
    }

    [Fact]
    public void WatchUrl_BuildsTheCanonicalLink()
    {
        Assert.Equal("https://www.youtube.com/watch?v=uPZSSdkGQhM", PlaylistMapping.WatchUrl(VideoId));
    }
}
