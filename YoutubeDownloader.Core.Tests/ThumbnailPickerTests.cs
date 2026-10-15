using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class ThumbnailPickerTests
{
    private const int MinWidth = 240;

    [Fact]
    public void Pick_TypicalYouTubeSet_ReturnsSmallestAtLeastMinWidth()
    {
        var thumbnails = new[]
        {
            ("https://i.ytimg.com/vi/x/default.jpg", 120, 90),
            ("https://i.ytimg.com/vi/x/mqdefault.jpg", 320, 180),
            ("https://i.ytimg.com/vi/x/hqdefault.jpg", 480, 360),
            ("https://i.ytimg.com/vi/x/sddefault.jpg", 640, 480),
            ("https://i.ytimg.com/vi/x/maxresdefault.jpg", 1280, 720),
        };

        Assert.Equal("https://i.ytimg.com/vi/x/mqdefault.jpg", ThumbnailPicker.Pick(thumbnails, MinWidth));
    }

    [Fact]
    public void Pick_ExactWidthMatch_Counts()
    {
        var thumbnails = new[] { ("a", 240, 135), ("b", 320, 180) };

        Assert.Equal("a", ThumbnailPicker.Pick(thumbnails, MinWidth));
    }

    [Fact]
    public void Pick_NoneWideEnough_ReturnsLargest()
    {
        var thumbnails = new[] { ("a", 120, 90), ("b", 168, 94) };

        Assert.Equal("b", ThumbnailPicker.Pick(thumbnails, MinWidth));
    }

    [Fact]
    public void Pick_Empty_ReturnsEmptyString()
    {
        Assert.Equal("", ThumbnailPicker.Pick(Array.Empty<(string, int, int)>(), MinWidth));
    }

    [Fact]
    public void Pick_BlankUrlsIgnored()
    {
        var thumbnails = new[] { ("", 320, 180), ("b", 480, 360) };

        Assert.Equal("b", ThumbnailPicker.Pick(thumbnails, MinWidth));
    }
}
