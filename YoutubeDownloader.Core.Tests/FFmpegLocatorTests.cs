using Moq;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegLocatorTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-locator-tests", Guid.NewGuid().ToString("N"));

    private static void WriteStub(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), "stub");
    }

    private string[] StagingFolders() =>
        Directory.Exists(Path.GetDirectoryName(_dir)!)
            ? Directory.GetDirectories(Path.GetDirectoryName(_dir)!, Path.GetFileName(_dir) + ".staging-*")
            : [];

    [Fact]
    public async Task EnsureAsync_WhenFfmpegMissing_DownloadsThenReturnsPath()
    {
        string? downloadedInto = null;
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, _, _) =>
            {
                downloadedInto = target;
                WriteStub(target, "ffmpeg.exe");
            })
            .Returns(Task.CompletedTask);

        var locator = new FFmpegLocator(downloader.Object, _dir);

        var path = await locator.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), path);
        downloader.Verify(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(downloadedInto);
        Assert.NotEqual(_dir, downloadedInto);
        Assert.True(File.Exists(Path.Combine(_dir, "ffmpeg.exe")));
        Assert.False(Directory.Exists(downloadedInto));
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

    [Fact]
    public async Task EnsureAsync_DownloaderThrows_LeavesNoFfmpegAndRetriesNextTime()
    {
        var calls = 0;
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, IProgress<double>?, CancellationToken>((target, _, _) =>
            {
                Directory.CreateDirectory(target);
                if (Interlocked.Increment(ref calls) == 1)
                    return Task.FromException(new HttpRequestException("boom"));
                WriteStub(target, "ffmpeg.exe");
                return Task.CompletedTask;
            });

        var locator = new FFmpegLocator(downloader.Object, _dir);

        await Assert.ThrowsAsync<HttpRequestException>(() => locator.EnsureAsync(null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_dir, "ffmpeg.exe")));
        Assert.Empty(StagingFolders());

        var path = await locator.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), path);
        downloader.Verify(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureAsync_DownloadWithoutFfmpeg_ThrowsAndPublishesNothing()
    {
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, _, _) => WriteStub(target, "ffprobe.exe"))
            .Returns(Task.CompletedTask);

        var locator = new FFmpegLocator(downloader.Object, _dir);

        await Assert.ThrowsAsync<FileNotFoundException>(() => locator.EnsureAsync(null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_dir, "ffprobe.exe")));
        Assert.False(File.Exists(Path.Combine(_dir, "ffmpeg.exe")));
    }

    [Fact]
    public async Task EnsureAsync_Cancelled_ReturnsImmediatelyAndInstallCompletesInBackground()
    {
        var tcs = new TaskCompletionSource();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, _, _) => received.TrySetResult(target))
            .Returns(tcs.Task);

        var locator = new FFmpegLocator(downloader.Object, _dir);
        using var cts = new CancellationTokenSource();

        var pending = locator.EnsureAsync(null, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));

        var target = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        WriteStub(target, "ffmpeg.exe");
        tcs.SetResult();

        await WaitUntilAvailableAsync(locator);
        var path = await locator.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), path);
        downloader.Verify(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureAsync_AfterCancel_ProgressNoLongerReachesCancelledCaller()
    {
        var tcs = new TaskCompletionSource();
        var received = new TaskCompletionSource<(string Target, IProgress<double> Progress)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, p, _) => received.TrySetResult((target, p!)))
            .Returns(tcs.Task);

        var locator = new FFmpegLocator(downloader.Object, _dir);
        var sink = new ListProgress();
        using var cts = new CancellationTokenSource();

        var pending = locator.EnsureAsync(sink, cts.Token);
        var (target, relay) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        relay.Report(0.3);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        relay.Report(0.6);

        lock (sink.Values)
        {
            Assert.Contains(0.3, sink.Values);
            Assert.DoesNotContain(0.6, sink.Values);
        }

        WriteStub(target, "ffmpeg.exe");
        tcs.SetResult();
        await WaitUntilAvailableAsync(locator);
    }

    [Fact]
    public async Task EnsureAsync_ConcurrentCalls_DownloadOnce()
    {
        var tcs = new TaskCompletionSource();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new Mock<IFFmpegBinaryDownloader>();
        downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<double>?, CancellationToken>((target, _, _) => received.TrySetResult(target))
            .Returns(tcs.Task);

        var locator = new FFmpegLocator(downloader.Object, _dir);

        var first = locator.EnsureAsync(null, CancellationToken.None);
        var second = locator.EnsureAsync(null, CancellationToken.None);

        var target = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        WriteStub(target, "ffmpeg.exe");
        tcs.SetResult();

        var paths = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(paths, p => Assert.Equal(Path.Combine(_dir, "ffmpeg.exe"), p));
        downloader.Verify(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void IsAvailable_ReflectsFfmpegPresence()
    {
        var locator = new FFmpegLocator(new Mock<IFFmpegBinaryDownloader>().Object, _dir);

        Assert.False(locator.IsAvailable);

        WriteStub(_dir, "ffmpeg.exe");

        Assert.True(locator.IsAvailable);
    }

    private static async Task WaitUntilAvailableAsync(FFmpegLocator locator)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!locator.IsAvailable && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(locator.IsAvailable);
    }

    private sealed class ListProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value)
        {
            lock (Values)
                Values.Add(value);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

        foreach (var staging in StagingFolders())
            Directory.Delete(staging, recursive: true);
    }
}
