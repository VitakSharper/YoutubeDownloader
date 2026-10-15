using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class QueueFormatResolverTests
{
    private static readonly AudioStreamOption OpusStream = new("opus", 160, "webm", Source: null!);

    /// <summary>A video offering these MP4 heights and no Opus stream.</summary>
    private static VideoInfo Info(params int[] heights) => new(
        SourceUrl: "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
        Title: "Some Title",
        Author: "Some Channel",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions: heights.Select(h => new VideoStreamOption($"{h}p", h, "mp4", Source: null!)).ToArray());

    private static int[] Heights(string commaList) => commaList.Split(',').Select(int.Parse).ToArray();

    [Fact]
    public void Mp3_KeepsTheQuality()
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Mp3(Mp3Quality.Cbr320), Info(720));

        Assert.Equal(new OutputFormat.Mp3(Mp3Quality.Cbr320), format);
    }

    [Fact]
    public void Opus_WithOpusStream_ResolvesToOpus()
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Opus(), Info(720) with { BestOpusAudio = OpusStream });

        Assert.IsType<OutputFormat.Opus>(format);
    }

    [Fact]
    public void Opus_WithoutOpusStream_ResolvesToNull()
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Opus(), Info(720));

        Assert.Null(format);
    }

    [Theory]
    [InlineData(1080, "2160,1440,1080,720", 1080)]
    [InlineData(720, "2160,1440,1080,720", 720)]
    [InlineData(480, "1080,720,360", 360)]
    public void Mp4_PicksTallestUnderCap(int maxHeight, string heights, int expectedHeight)
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Mp4(maxHeight), Info(Heights(heights)));

        var mp4 = Assert.IsType<OutputFormat.Mp4>(format);
        Assert.Equal(expectedHeight, mp4.Video.Height);
    }

    [Fact]
    public void Mp4_NothingUnderCap_FallsBackToSmallest()
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Mp4(240), Info(720, 480));

        var mp4 = Assert.IsType<OutputFormat.Mp4>(format);
        Assert.Equal(480, mp4.Video.Height);
    }

    [Fact]
    public void Mp4_NoVideoOptions_ResolvesToNull()
    {
        var format = QueueFormatResolver.Resolve(new QueueFormat.Mp4(VideoHeightCap.Default.MaxHeight), Info());

        Assert.Null(format);
    }

    [Fact]
    public void Extension_PerFormat()
    {
        Assert.Equal("mp3", QueueFormatResolver.Extension(new QueueFormat.Mp3(Mp3Quality.BestVbr)));
        Assert.Equal("opus", QueueFormatResolver.Extension(new QueueFormat.Opus()));
        Assert.Equal("mp4", QueueFormatResolver.Extension(new QueueFormat.Mp4(1080)));
    }

    [Fact]
    public void HeightCaps_Are2160To360_DefaultIs1080()
    {
        Assert.Equal(new[] { 2160, 1440, 1080, 720, 480, 360 }, VideoHeightCap.All.Select(c => c.MaxHeight));
        Assert.Equal(1080, VideoHeightCap.Default.MaxHeight);
        Assert.Contains(VideoHeightCap.Default, VideoHeightCap.All);
        Assert.Equal("1080p", VideoHeightCap.Default.ToString());
    }

    [Fact]
    public void HeightCaps_LabelAv1AboveFullHd()
    {
        foreach (var cap in VideoHeightCap.All)
            Assert.Equal(cap.MaxHeight > 1080, cap.Label.Contains("AV1", StringComparison.Ordinal));
    }
}
