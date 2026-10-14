using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class SaveFileTypesTests
{
    [Theory]
    [InlineData("mp3", "MP3 audio", "*.mp3")]
    [InlineData("opus", "Opus audio", "*.opus")]
    [InlineData("mp4", "MP4 video", "*.mp4")]
    [InlineData(".opus", "Opus audio", "*.opus")]
    [InlineData("OPUS", "Opus audio", "*.opus")]
    public void For_KnownExtension_ReturnsMatchingFilter(string ext, string label, string pattern)
    {
        var type = SaveFileTypes.For(ext);

        Assert.Equal(label, type.Label);
        Assert.Equal(pattern, type.Pattern);
    }

    [Fact]
    public void For_Opus_IsNotTheMp4Filter()
    {
        Assert.NotEqual("*.mp4", SaveFileTypes.For("opus").Pattern);
    }

    [Fact]
    public void For_UnknownExtension_FallsBackToGenericFilterForThatExtension()
    {
        var type = SaveFileTypes.For("mkv");

        Assert.Equal("MKV file", type.Label);
        Assert.Equal("*.mkv", type.Pattern);
    }
}
