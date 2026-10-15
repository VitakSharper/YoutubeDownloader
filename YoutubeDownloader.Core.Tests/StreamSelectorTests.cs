using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class StreamSelectorTests
{
    private static AudioStreamOption Audio(int kbps, string container = "mp4")
        => new("aac", kbps, container, Source: null!);

    private static AudioStreamOption Opus(int kbps, bool? original = null)
        => new("opus", kbps, "webm", Source: null!, IsOriginalLanguage: original);

    private static AudioStreamOption Aac(int kbps, string container = "mp4", bool? original = null)
        => new("mp4a.40.2", kbps, container, Source: null!, IsOriginalLanguage: original);

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

    [Fact]
    public void PickBestOpusAudio_ReturnsHighestBitrateOpus_IgnoringOtherCodecs()
    {
        var best = StreamSelector.PickBestOpusAudio(new[] { Audio(320), Opus(96), Opus(160), Audio(256) });
        Assert.NotNull(best);
        Assert.Equal(160, best!.BitrateKbps);
        Assert.Equal("opus", best.Codec);
    }

    [Fact]
    public void PickBestOpusAudio_ReturnsNull_WhenNoOpusStream()
    {
        var best = StreamSelector.PickBestOpusAudio(new[] { Audio(128), Audio(256) });
        Assert.Null(best);
    }

    [Fact]
    public void PickBestMp4Audio_ReturnsHighestBitrateAac_IgnoringOpus()
    {
        var best = StreamSelector.PickBestMp4Audio(new[] { Opus(160), Aac(48), Aac(128) });
        Assert.NotNull(best);
        Assert.Equal(128, best!.BitrateKbps);
        Assert.Equal("mp4a.40.2", best.Codec);
    }

    [Fact]
    public void PickBestMp4Audio_ReturnsNull_WhenOnlyOpus()
    {
        var best = StreamSelector.PickBestMp4Audio(new[] { Opus(160), Opus(70) });
        Assert.Null(best);
    }

    [Fact]
    public void PickBestMp4Audio_IgnoresMp4aInNonMp4Container()
    {
        var best = StreamSelector.PickBestMp4Audio(new[] { Aac(128, container: "webm") });
        Assert.Null(best);
    }

    // An English video with AI-dubbed tracks: in each codec a dubbed track out-bitrates the original.
    private static AudioStreamOption[] DubbedSet(out AudioStreamOption originalOpus, out AudioStreamOption originalAac)
    {
        originalOpus = Opus(130, original: true);
        originalAac = Aac(128, original: true);
        return new[] { Opus(160, original: false), Opus(70, original: true), originalOpus, Aac(192, original: false), originalAac };
    }

    [Fact]
    public void PickBestAudio_DubbedSet_ReturnsBestOriginalLanguageStream()
    {
        var input = DubbedSet(out var originalOpus, out _);
        Assert.Same(originalOpus, StreamSelector.PickBestAudio(input));
    }

    [Fact]
    public void PickBestOpusAudio_DubbedSet_ReturnsOriginalLanguageOpus()
    {
        var input = DubbedSet(out var originalOpus, out _);
        Assert.Same(originalOpus, StreamSelector.PickBestOpusAudio(input));
    }

    [Fact]
    public void PickBestMp4Audio_DubbedSet_ReturnsOriginalLanguageAac()
    {
        var input = DubbedSet(out _, out var originalAac);
        Assert.Same(originalAac, StreamSelector.PickBestMp4Audio(input));
    }

    [Theory]
    [InlineData(null)]  // the manifest gave no language data
    [InlineData(false)] // language data, but no track flagged as the original
    public void Pickers_NoOriginalLanguageFlagged_PickHighestBitrate(bool? original)
    {
        var bestOpus = Opus(160, original);
        var bestAac = Aac(192, original: original);
        var input = new[] { Opus(96, original), bestOpus, Aac(128, original: original), bestAac };

        Assert.Same(bestAac, StreamSelector.PickBestAudio(input));
        Assert.Same(bestOpus, StreamSelector.PickBestOpusAudio(input));
        Assert.Same(bestAac, StreamSelector.PickBestMp4Audio(input));
    }

    [Fact]
    public void Pickers_OriginalLanguageOnlyInOpus_Mp4StillFindsBestAac()
    {
        var originalOpus = Opus(96, original: true);
        var bestAac = Aac(192, original: false);
        var input = new[] { Opus(160, original: false), originalOpus, Aac(128, original: false), bestAac };

        Assert.Same(originalOpus, StreamSelector.PickBestOpusAudio(input));
        Assert.Same(originalOpus, StreamSelector.PickBestAudio(input));
        Assert.Same(bestAac, StreamSelector.PickBestMp4Audio(input)); // not null: no needless re-encode fallback
    }
}
