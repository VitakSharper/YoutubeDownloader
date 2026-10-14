using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class DownloadProgressAggregatorTests
{
    [Fact]
    public void SingleFile_ReportsDownloadedOverTotal()
    {
        var sut = new DownloadProgressAggregator();

        Assert.Equal(0.5, sut.Report(50, 100), precision: 6);
    }

    [Fact]
    public void TwoFiles_CombinesBothTotals()
    {
        var sut = new DownloadProgressAggregator();

        Assert.Equal(0.0, sut.Report(0, 100), precision: 6);
        Assert.Equal(0.0, sut.Report(0, 300), precision: 6);
        Assert.Equal(0.25, sut.Report(100, 300), precision: 6);
        Assert.Equal(0.5, sut.Report(100, 100), precision: 6);
    }

    [Fact]
    public void SentinelReports_AreIgnored()
    {
        var sut = new DownloadProgressAggregator();
        sut.Report(50, 100);

        Assert.Equal(0.5, sut.Report(1, 1), precision: 6);
    }

    [Fact]
    public void OutOfOrderReports_NeverDecrease()
    {
        var sut = new DownloadProgressAggregator();
        sut.Report(80, 100);

        Assert.Equal(0.8, sut.Report(40, 100), precision: 6);
    }

    [Fact]
    public void CompletedDownloads_ReachOne()
    {
        var sut = new DownloadProgressAggregator();
        sut.Report(100, 100);

        Assert.Equal(1.0, sut.Report(300, 300), precision: 6);
    }
}
