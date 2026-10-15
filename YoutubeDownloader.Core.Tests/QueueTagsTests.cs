using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class QueueTagsTests
{
    private const string WatchUrl = "https://www.youtube.com/watch?v=uPZSSdkGQhM";

    private static readonly QueueItem Item = new(
        VideoId: "uPZSSdkGQhM",
        Url: WatchUrl,
        Title: "Title as listed",
        Author: "Channel as listed",
        Duration: TimeSpan.FromMinutes(3),
        Index: 1,
        ThumbnailUrl: null);

    private static VideoInfo Info(DateTimeOffset? uploadDate) => new(
        SourceUrl: WatchUrl + "&list=PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e",
        Title: "Artist - Song",
        Author: "Artist - Topic",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "https://i.ytimg.com/vi/uPZSSdkGQhM/mqdefault.jpg",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions: [],
        UploadDate: uploadDate);

    [Fact]
    public void For_UsesInfoTitleChannelAndItemUrlAndYear()
    {
        var tags = QueueTags.For(Item, Info(new DateTimeOffset(2009, 10, 25, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new MediaTags("Artist - Song", "Artist - Topic", WatchUrl, 2009), tags);
    }

    [Fact]
    public void For_WithoutUploadDate_HasNoYear()
    {
        var tags = QueueTags.For(Item, Info(uploadDate: null));

        Assert.Null(tags.Year);
    }
}
