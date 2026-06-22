using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class QualityAdvisorTests
{
    [Theory]
    [InlineData(128, 320, true)]   // target higher than source
    [InlineData(192, 192, false)]  // equal
    [InlineData(256, 128, false)]  // target lower
    public void WouldUpscale_ComparesTargetToSource(int source, int target, bool expected)
    {
        Assert.Equal(expected, QualityAdvisor.WouldUpscale(source, target));
    }

    [Fact]
    public void WouldUpscale_UnknownSource_ReturnsFalse()
    {
        // source <= 0 means "unknown"; never warn.
        Assert.False(QualityAdvisor.WouldUpscale(0, 320));
    }

    [Fact]
    public void Describe_FormatsCodecAndBitrate()
    {
        Assert.Equal("opus, 160 kbps", QualityAdvisor.Describe("opus", 160));
    }
}
