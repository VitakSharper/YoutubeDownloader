using Moq;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegLocatorTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-locator-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureAsync_WhenFfmpegMissing_DownloadsThenReturnsPath()
    {
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(_dir, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, _, _) =>
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "ffmpeg.exe"), "stub");
            })
            .Returns(Task.CompletedTask);

        var locator = new FFmpegLocator(downloader.Object, _dir);

        var path = await locator.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), path);
        downloader.Verify(d => d.DownloadAsync(_dir, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAsync_WhenFfmpegPresent_DoesNotDownload()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ffmpeg.exe"), "stub");

        var downloader = new Mock<IFFmpegBinaryDownloader>();
        var locator = new FFmpegLocator(downloader.Object, _dir);

        var path = await locator.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), path);
        downloader.Verify(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
