using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("My Video", "My Video")]
    [InlineData("a/b:c*d?", "a_b_c_d_")]
    [InlineData("trailing dot.", "trailing dot")]
    public void Sanitize_RemovesInvalidCharsAndTrailingDots(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_EmptyOrWhitespace_ReturnsFallback()
    {
        Assert.Equal("download", FileNameSanitizer.Sanitize("   "));
    }
}
