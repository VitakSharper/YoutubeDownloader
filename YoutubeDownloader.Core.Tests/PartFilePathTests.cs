using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

public class PartFilePathTests
{
    [Theory]
    [InlineData("song.mp3", "song.part.mp3")]
    [InlineData(@"C:\out\Mr. Brightside.opus", @"C:\out\Mr. Brightside.part.opus")]
    [InlineData(@"C:\out\clip.mp4", @"C:\out\clip.part.mp4")]
    public void For_InsertsPartBeforeRealExtension(string target, string expected)
    {
        Assert.Equal(expected, PartFilePath.For(target));
    }

    [Fact]
    public void For_KeepsTheTargetDirectory()
    {
        const string target = @"C:\out\sub\song.mp3";

        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(PartFilePath.For(target)));
    }
}
