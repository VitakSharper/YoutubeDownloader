using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class Mp3QualityTests
{
    [Fact]
    public void All_ListsBestVbrFirstThenCbrDescending()
    {
        Assert.Equal(
            new[] { Mp3Quality.BestVbr, Mp3Quality.Cbr320, Mp3Quality.Cbr256, Mp3Quality.Cbr192, Mp3Quality.Cbr128 },
            Mp3Quality.All);
    }

    [Fact]
    public void BestVbr_IsVbrLevelZeroApprox245()
    {
        Assert.True(Mp3Quality.BestVbr.IsVbr);
        Assert.Equal(0, Mp3Quality.BestVbr.VbrQuality);
        Assert.Equal(245, Mp3Quality.BestVbr.ApproxKbps);
        Assert.Equal("Best (VBR V0)", Mp3Quality.BestVbr.Label);
        Assert.Equal("VBR V0 (~245 kbps)", Mp3Quality.BestVbr.HistoryLabel);
    }

    [Fact]
    public void Cbr320_IsConstant320()
    {
        Assert.False(Mp3Quality.Cbr320.IsVbr);
        Assert.Equal(320, Mp3Quality.Cbr320.ApproxKbps);
        Assert.Equal("320 kbps", Mp3Quality.Cbr320.Label);
        Assert.Equal("320 kbps", Mp3Quality.Cbr320.HistoryLabel);
    }

    [Fact]
    public void ToString_ReturnsLabel()
    {
        Assert.Equal("Best (VBR V0)", Mp3Quality.BestVbr.ToString());
    }
}
