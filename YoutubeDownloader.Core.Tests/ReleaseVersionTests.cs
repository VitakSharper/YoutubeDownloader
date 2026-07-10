using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", true, "1.2.3")]
    [InlineData("1.2.3", true, "1.2.3")]
    [InlineData("V2.0", true, "2.0")]
    [InlineData("v1.0.0.0", true, "1.0.0.0")]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    [InlineData("beta", false, null)]
    [InlineData(null, false, null)]
    public void TryParseTag_parses_expected(string? tag, bool ok, string? expected)
    {
        var result = ReleaseVersion.TryParseTag(tag, out var version);
        Assert.Equal(ok, result);
        if (ok)
            Assert.Equal(Version.Parse(expected!), version);
    }

    [Fact]
    public void Newer_tag_compares_greater_than_current()
    {
        Assert.True(ReleaseVersion.TryParseTag("v1.1.0", out var latest));
        Assert.True(latest > new Version(1, 0, 0));
    }
}
