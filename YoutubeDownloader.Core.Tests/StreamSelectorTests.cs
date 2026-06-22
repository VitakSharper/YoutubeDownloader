using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class StreamSelectorTests
{
    private static AudioStreamOption Audio(int kbps, string container = "mp4")
        => new("aac", kbps, container, Source: null!);

    private static VideoStreamOption Video(string label, int height, string container)
        => new(label, height, container, Source: null!);

    [Fact]
    public void PickBestAudio_ReturnsHighestBitrate()
    {
        var best = StreamSelector.PickBestAudio(new[] { Audio(96), Audio(160), Audio(128) });
        Assert.Equal(160, best.BitrateKbps);
    }

    [Fact]
    public void PickBestAudio_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => StreamSelector.PickBestAudio(Array.Empty<AudioStreamOption>()));
    }

    [Fact]
    public void OrderVideoOptions_KeepsMp4Only_DescendingByHeight_Distinct()
    {
        var input = new[]
        {
            Video("1080p", 1080, "mp4"),
            Video("360p", 360, "mp4"),
            Video("1080p60", 1080, "webm"), // dropped: webm
            Video("720p", 720, "mp4"),
            Video("1080p", 1080, "mp4"),    // dropped: duplicate label
        };

        var result = StreamSelector.OrderVideoOptions(input);

        Assert.Equal(new[] { "1080p", "720p", "360p" }, result.Select(v => v.QualityLabel).ToArray());
    }
}
