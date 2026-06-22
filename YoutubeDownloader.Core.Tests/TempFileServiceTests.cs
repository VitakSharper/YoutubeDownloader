using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class TempFileServiceTests
{
    [Fact]
    public void NewTempFile_ReturnsPathWithRequestedExtension()
    {
        var svc = new TempFileService();
        var path = svc.NewTempFile("mp3");
        Assert.EndsWith(".mp3", path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void NewTempFile_NormalizesLeadingDot()
    {
        var svc = new TempFileService();
        var path = svc.NewTempFile(".mp4");
        Assert.EndsWith(".mp4", path);
        Assert.DoesNotContain("..mp4", path);
    }

    [Fact]
    public void NewTempFile_ProducesUniquePaths()
    {
        var svc = new TempFileService();
        Assert.NotEqual(svc.NewTempFile("mp3"), svc.NewTempFile("mp3"));
    }
}
