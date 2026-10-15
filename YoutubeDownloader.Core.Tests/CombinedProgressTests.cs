using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class CombinedProgressTests
{
    [Fact]
    public void ReportFirst_WeightsFirstSource()
    {
        var sut = new CombinedProgress(0.8);

        var value = sut.ReportFirst(0.5);

        Assert.Equal(0.4, value, precision: 6);
    }

    [Fact]
    public void BothComplete_ReachesOne()
    {
        var sut = new CombinedProgress(0.8);

        sut.ReportFirst(1.0);
        var value = sut.ReportSecond(1.0);

        Assert.Equal(1.0, value, precision: 6);
    }

    [Fact]
    public void LowerLateReport_DoesNotDecrease()
    {
        var sut = new CombinedProgress(0.8);

        Assert.Equal(0.4, sut.ReportFirst(0.5), precision: 6);
        Assert.Equal(0.6, sut.ReportSecond(1.0), precision: 6);

        // A stale, lower report from the first source must not pull the bar back.
        Assert.Equal(0.6, sut.ReportFirst(0.3), precision: 6);
    }

    [Fact]
    public void OutOfRangeFractions_AreClamped()
    {
        var sut = new CombinedProgress(0.8);

        Assert.Equal(0.8, sut.ReportFirst(1.5), precision: 6);
        Assert.Equal(0.8, sut.ReportSecond(-1), precision: 6);
    }

    [Fact]
    public void Constructor_RejectsWeightOutsideZeroToOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombinedProgress(1.5));
    }
}
