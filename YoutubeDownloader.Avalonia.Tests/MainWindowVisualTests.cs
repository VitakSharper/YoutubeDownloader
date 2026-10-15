using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Avalonia.Tests;

/// <summary>
/// Renders the real MainWindow (App.axaml styles, Skia) in states prepared through mocked services,
/// asserts what the user must see, and saves a PNG when YTDL_SNAPSHOT_DIR is set (a scratch folder,
/// never the repo). This is how the UI is checked visually: in-process rendering, not screen capture.
/// </summary>
public class MainWindowVisualTests
{
    private const string VideoUrl = "https://youtu.be/jNQXAC9IVRw";

    private readonly Mock<IYouTubeService> _youtube = new();
    private readonly Mock<IFFmpegLocator> _ffmpeg = new();
    private readonly Mock<IMediaConverter> _converter = new();
    private readonly Mock<ISaveFileService> _saveFile = new();
    private readonly Mock<ITempFileService> _temp = new();
    private readonly Mock<IHistoryStore> _history = new();
    private readonly Mock<ISettingsStore> _settings = new();
    private readonly Mock<IConfirmationService> _confirm = new();
    private readonly Mock<IUpdateService> _updates = new();
    private readonly Mock<ISelfUpdater> _selfUpdater = new();

    public MainWindowVisualTests()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>());
        _settings.Setup(s => s.Load()).Returns(new AppSettings());
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _updates.Setup(u => u.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateInfo(new Version(1, 5, 0, 0), new Version(1, 5, 0), false, null, null, null, null));
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns<string>(ext => $@"C:\scratch\tmp.{ext}");
    }

    [AvaloniaFact]
    public async Task RestoredSettings_ShowLastFormatAndMp3Quality()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "AudioMp3", LastMp3Quality = "cbr-256" });
        var (window, vm, _) = Create();

        await FetchAsync(vm, SampleInfo());

        Assert.Equal(Mp3Quality.Cbr256, vm.SelectedQuality);
        var texts = VisibleTexts(window);
        Assert.Contains("256 kbps", texts);
        Assert.Contains("MP3 QUALITY", texts);
        Assert.DoesNotContain("VIDEO QUALITY", texts);
        Snapshot(window, "016-restored-mp3-256");
    }

    [AvaloniaFact]
    public async Task OpusMode_ShowsNoQualityPicker()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "AudioOpus" });
        var (window, vm, _) = Create();

        await FetchAsync(vm, SampleInfo());

        var texts = VisibleTexts(window);
        Assert.DoesNotContain("MP3 QUALITY", texts);
        Assert.DoesNotContain("VIDEO QUALITY", texts);
        Snapshot(window, "opus-no-pickers");
    }

    [AvaloniaFact]
    public async Task RestoredSettings_VideoMode_ShowsVideoQualityPicker()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "Video" });
        var (window, vm, _) = Create();

        await FetchAsync(vm, SampleInfo());

        Assert.Equal(DownloadMode.Video, vm.SelectedMode);
        var texts = VisibleTexts(window);
        Assert.Contains("720p", texts);
        Assert.Contains("VIDEO QUALITY", texts);
        Assert.DoesNotContain("MP3 QUALITY", texts);
        Snapshot(window, "016-restored-video");
    }

    [AvaloniaFact]
    public void StartupCheck_WithNewerRelease_ShowsBannerAndDownloadButton()
    {
        _updates.Setup(u => u.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(NewerRelease());
        var (window, _, update) = Create();

        Assert.True(update.IsUpdateAvailable);
        Assert.Contains("Version 1.6.0 is available (you have 1.5.0.0).", VisibleTexts(window));
        Snapshot(window, "017a-startup-banner");
    }

    [AvaloniaFact]
    public void StartupCheck_WhenUpToDate_StaysSilent()
    {
        var (window, _, update) = Create();

        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("", update.Status);
        Snapshot(window, "017a-startup-silent");
    }

    [AvaloniaFact]
    public void UpdateDownload_ShowsProgressBarUnderTheButtons()
    {
        _updates.Setup(u => u.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(NewerRelease());
        var hold = new TaskCompletionSource<string>();
        _updates.Setup(u => u.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns<UpdateInfo, IProgress<double>?, CancellationToken>((_, progress, _) =>
            {
                progress?.Report(0.4);
                return hold.Task;
            });
        var (window, _, update) = Create();

        _ = update.DownloadUpdateCommand.ExecuteAsync(null);
        Settle();

        Assert.True(update.IsDownloadingUpdate);
        Assert.Equal(0.4, update.Progress, 3);
        var bar = window.FindControl<StackPanel>("UpdatePanel")!.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.True(bar.IsEffectivelyVisible);
        Snapshot(window, "014-update-progress-40");

        hold.SetResult(@"C:\scratch\new.exe");
        Settle();
        Assert.False(bar.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void SelfUpdate_WhenEligible_ShowsInstallingStatus()
    {
        _updates.Setup(u => u.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(NewerRelease());
        _updates.Setup(u => u.DownloadAndExtractAsync(It.IsAny<UpdateInfo>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\scratch\new.exe");
        _selfUpdater.Setup(s => s.CanSelfUpdate).Returns(true);
        var (window, _, update) = Create();

        _ = update.DownloadUpdateCommand.ExecuteAsync(null);
        Settle();

        _selfUpdater.Verify(s => s.ApplyAndRestart(@"C:\scratch\new.exe"), Times.Once);
        Assert.Contains("Installing update…", VisibleTexts(window));
        Snapshot(window, "017b-installing");
    }

    [AvaloniaFact]
    public async Task VideoDownload_ShowsCombinedProgressOfBothStreams()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "Video" });
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(@"C:\scratch\Me at the zoo.mp4");
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ffmpeg.exe");
        var info = SampleInfo();
        // Video stream half done, audio stream finished; both then wait until cancelled.
        _youtube.Setup(y => y.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns<IStreamInfo, string, IProgress<double>?, CancellationToken>((source, _, progress, ct) =>
            {
                progress?.Report(source == info.VideoOptions[0].Source ? 0.5 : 1.0);
                return Task.Delay(Timeout.Infinite, ct);
            });
        var (window, vm, _) = Create();
        await FetchAsync(vm, info);

        _ = vm.DownloadCommand.ExecuteAsync(null);
        Settle();

        // 0.1 + (0.5 × 80 % video + 1.0 × 20 % audio) × 0.6 = 0.46
        Assert.Equal(0.46, vm.Progress, 3);
        Assert.Contains("Downloading video (720p)…", VisibleTexts(window));
        Snapshot(window, "010-video-progress-46");

        await vm.CancelDownloadCommand.ExecuteAsync(null);
        Settle();
        Assert.Equal("Download cancelled.", vm.StatusMessage);
    }

    /// <summary>
    /// Needs the network (fetches a real YouTube thumbnail through AsyncImage), so it only runs when
    /// asked for explicitly. Checks plan 011's small thumbnail still fills the 120×68 box cleanly.
    /// </summary>
    [AvaloniaFact(Explicit = true)]
    public async Task Thumbnail_FromYouTube_FillsTheBox()
    {
        var (window, vm, _) = Create();
        await FetchAsync(vm, SampleInfo() with { ThumbnailUrl = "https://i.ytimg.com/vi/jNQXAC9IVRw/mqdefault.jpg" });

        var image = window.GetVisualDescendants().OfType<Image>()
            .Single(i => !string.IsNullOrEmpty(Imaging.AsyncImage.GetSourceUrl(i)));
        for (var i = 0; i < 150 && image.Source is null; i++)
        {
            await Task.Delay(100);
            Settle();
        }

        Assert.NotNull(image.Source);
        Snapshot(window, "011-thumbnail");
    }

    private (MainWindow Window, MainViewModel Vm, UpdateViewModel Update) Create()
    {
        var vm = new MainViewModel(_youtube.Object, _ffmpeg.Object,
            new DownloadPipeline(_youtube.Object, _converter.Object, _temp.Object, Mock.Of<IOutputFileService>(), Mock.Of<IThumbnailFetcher>()),
            _saveFile.Object, _history.Object, Mock.Of<ILinkOpener>(), Mock.Of<IFileRevealer>(), _settings.Object,
            Mock.Of<IClipboardService>(), _confirm.Object, Mock.Of<IFoobar2000Launcher>());
        var update = new UpdateViewModel(_updates.Object, Mock.Of<IFileRevealer>(), _selfUpdater.Object);
        var window = new MainWindow(vm, new AvaloniaClipboardService(), update);
        window.Show(); // raises Opened → the silent startup update check
        Settle();
        return (window, vm, update);
    }

    private async Task FetchAsync(MainViewModel vm, VideoInfo info)
    {
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        vm.Url = VideoUrl;
        await vm.FetchInfoCommand.ExecuteAsync(null);
        Settle();
    }

    private static VideoInfo SampleInfo() => new(
        SourceUrl: VideoUrl,
        Title: "Me at the zoo",
        Author: "jawed",
        Duration: TimeSpan.FromSeconds(19),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Mock.Of<IStreamInfo>()),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Mock.Of<IStreamInfo>()) },
        BestOpusAudio: new AudioStreamOption("opus", 128, "webm", Mock.Of<IStreamInfo>()),
        BestMp4Audio: new AudioStreamOption("mp4a.40.2", 128, "mp4", Mock.Of<IStreamInfo>()));

    private static UpdateInfo NewerRelease() =>
        new(new Version(1, 5, 0, 0), new Version(1, 6, 0), true, "https://example.invalid/app.zip", "app.zip", null, null);

    /// <summary>Runs queued dispatcher work (bindings, Progress&lt;T&gt; posts, layout).</summary>
    private static void Settle()
    {
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static List<string> VisibleTexts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.Text!)
            .ToList();

    private static void Snapshot(Window window, string name)
    {
        Settle();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var dir = Environment.GetEnvironmentVariable("YTDL_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
