using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class HistoryDateLabelTests
{
    private static readonly DateOnly Today = new(2026, 6, 22);

    [Fact]
    public void For_Today_ReturnsToday()
        => Assert.Equal("Today", HistoryDateLabel.For(Today, Today));

    [Fact]
    public void For_Yesterday_ReturnsYesterday()
        => Assert.Equal("Yesterday", HistoryDateLabel.For(Today.AddDays(-1), Today));

    [Fact]
    public void For_OlderDate_ReturnsFullDate()
    {
        var label = HistoryDateLabel.For(new DateOnly(2026, 6, 16), Today);
        Assert.Contains("2026", label);
        Assert.Contains("16", label);
        Assert.NotEqual("Today", label);
        Assert.NotEqual("Yesterday", label);
    }
}
