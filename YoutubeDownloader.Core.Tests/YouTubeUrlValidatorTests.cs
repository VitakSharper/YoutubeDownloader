using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class YouTubeUrlValidatorTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ")]
    public void IsValid_ReturnsTrue_ForRecognizedIdsAndUrls(string url)
    {
        Assert.True(YouTubeUrlValidator.IsValid(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/not-a-video")]
    [InlineData("hello world")]
    public void IsValid_ReturnsFalse_ForGarbage(string? url)
    {
        Assert.False(YouTubeUrlValidator.IsValid(url));
    }

    [Fact]
    public void GetVideoId_ExtractsId()
    {
        Assert.Equal("dQw4w9WgXcQ", YouTubeUrlValidator.GetVideoId("https://youtu.be/dQw4w9WgXcQ"));
    }
}
