using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class StreamMappingTests
{
    [Fact]
    public void ToAudioOption_CopiesCodecBitrateContainer()
    {
        var opt = StreamMapping.ToAudioOption("opus", bitsPerSecond: 160_000, container: "webm", source: null!);
        Assert.Equal("opus", opt.Codec);
        Assert.Equal(160, opt.BitrateKbps);     // bits/s -> kbps
        Assert.Equal("webm", opt.Container);
    }

    [Fact]
    public void ToVideoOption_CopiesLabelHeightContainer()
    {
        var opt = StreamMapping.ToVideoOption("1080p", 1080, "mp4", source: null!);
        Assert.Equal("1080p", opt.QualityLabel);
        Assert.Equal(1080, opt.Height);
        Assert.Equal("mp4", opt.Container);
    }
}
