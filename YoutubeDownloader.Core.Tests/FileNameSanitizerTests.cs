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

    [Fact]
    public void SanitizeWithMaxLength_CutsAndTrimsTrailingDotsAndSpaces()
    {
        Assert.Equal("abc", FileNameSanitizer.Sanitize("abc. def", 5));
    }

    [Fact]
    public void SanitizeWithMaxLength_ShortName_IsUnchanged()
    {
        Assert.Equal("Halo", FileNameSanitizer.Sanitize("Halo", 150));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("COM1", "_COM1")]
    [InlineData("LPT9", "_LPT9")]
    public void SanitizeWithMaxLength_ReservedDeviceName_IsPrefixed(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input, 150));
    }
}
