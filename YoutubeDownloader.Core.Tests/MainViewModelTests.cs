using Moq;
using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Core.Tests;

public class MainViewModelTests
{
    private readonly Mock<IYouTubeService> _youtube = new();
    private readonly Mock<IFFmpegLocator> _ffmpeg = new();
    private readonly Mock<IMediaConverter> _converter = new();
    private readonly Mock<ISaveFileService> _saveFile = new();
    private readonly Mock<ITempFileService> _temp = new();
    private readonly Mock<IHistoryStore> _history = new();
    private readonly Mock<ILinkOpener> _linkOpener = new();
    private readonly Mock<IFileRevealer> _fileRevealer = new();
    private readonly Mock<ISettingsStore> _settings = new();
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<IConfirmationService> _confirm = new();
    private readonly Mock<IFoobar2000Launcher> _foobar = new();
    private readonly Mock<IOutputFileService> _output = new();
    private readonly Mock<IThumbnailFetcher> _thumbnails = new();

    public MainViewModelTests()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>());
        _settings.Setup(s => s.Load()).Returns(new AppSettings());
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
    }

    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object,
            new DownloadPipeline(_youtube.Object, _converter.Object, _temp.Object, _output.Object, _thumbnails.Object),
            _saveFile.Object, _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object,
            _clipboard.Object, _confirm.Object, _foobar.Object);

    private static readonly IStreamInfo AacSource = Mock.Of<IStreamInfo>();

    private static VideoInfo SampleInfo(int sourceAudioKbps = 128, bool withOpus = true,
        string sourceUrl = "https://youtu.be/dQw4w9WgXcQ", bool withAac = false) => new(
        SourceUrl: sourceUrl,
        Title: "Test Video",
        Author: "Tester",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", sourceAudioKbps, "webm", Source: null!),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Source: null!) },
        BestOpusAudio: withOpus ? new AudioStreamOption("opus", 160, "webm", Source: null!) : null,
        BestMp4Audio: withAac ? new AudioStreamOption("mp4a.40.2", 128, "mp4", Source: AacSource) : null);

    [Fact]
    public async Task FetchInfo_WithInvalidUrl_SetsErrorAndDoesNotCallService()
    {
        var vm = CreateSut();
        vm.Url = "not a url";

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Contains("valid", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        _youtube.Verify(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchInfo_Success_PopulatesVideoInfoAndSelectsFirstQuality()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(info);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Same(info, vm.VideoInfo);
        Assert.Equal("720p", vm.SelectedVideoQuality!.QualityLabel);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task FetchInfo_ServiceThrows_ClearsInfoAndReportsError()
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Null(vm.VideoInfo);
        Assert.Contains("boom", vm.StatusMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void UpscaleWarning_ShownWhenQualityExceedsSource_InAudioMode()
    {
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.VideoInfo))!
            .SetValue(vm, SampleInfo(sourceAudioKbps: 128));
        vm.SelectedMode = DownloadMode.AudioMp3;

        vm.SelectedQuality = Mp3Quality.Cbr320;
        Assert.NotNull(vm.UpscaleWarning);

        vm.SelectedQuality = Mp3Quality.Cbr128;
        Assert.Null(vm.UpscaleWarning);
    }

    [Fact]
    public void UpscaleWarning_NeverShownInVideoMode()
    {
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.VideoInfo))!
            .SetValue(vm, SampleInfo(sourceAudioKbps: 128));
        vm.SelectedQuality = Mp3Quality.Cbr320;
        vm.SelectedMode = DownloadMode.Video;

        Assert.Null(vm.UpscaleWarning);
    }

    [Fact]
    public void CanDownload_FalseUntilVideoInfoLoaded()
    {
        var vm = CreateSut();
        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Download_AudioMode_ConvertsWithSelectedQuality()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;
        vm.SelectedQuality = Mp3Quality.Cbr256;

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.ConvertToMp3Async(
            @"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.part.mp3", Mp3Quality.Cbr256, It.IsAny<MediaTags?>(), It.IsAny<string?>(),
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Download_UserCancelsSaveDialog_DoesNothing()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync((string?)null);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        _ffmpeg.Verify(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _converter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Download_VideoMode_DownloadsBothStreamsAndMuxes()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp4", It.IsAny<string?>())).ReturnsAsync(@"C:\out\clip.mp4");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        await vm.DownloadCommand.ExecuteAsync(null);

        _youtube.Verify(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
            It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _converter.Verify(c => c.MuxToMp4Async(@"C:\ffmpeg\ffmpeg.exe", @"C:\temp\v.mp4", @"C:\temp\a.webm",
            @"C:\out\clip.part.mp4", info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Download_VideoMode_WithAacStream_DownloadsAacAndStreamCopies()
    {
        var info = SampleInfo(withAac: true);
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.mp4");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp4", It.IsAny<string?>())).ReturnsAsync(@"C:\out\clip.mp4");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        await vm.DownloadCommand.ExecuteAsync(null);

        _youtube.Verify(s => s.DownloadStreamAsync(AacSource, @"C:\temp\a.mp4",
            It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _temp.Verify(t => t.NewTempFile("mp4"), Times.Exactly(2));
        _converter.Verify(c => c.MuxToMp4CopyAudioAsync(@"C:\ffmpeg\ffmpeg.exe", @"C:\temp\v.mp4", @"C:\temp\a.mp4",
            It.IsAny<string>(), info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.MuxToMp4Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Download_VideoMode_WithoutAacStream_FallsBackToReencodeMux()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp4", It.IsAny<string?>())).ReturnsAsync(@"C:\out\clip.mp4");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.MuxToMp4Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.MuxToMp4CopyAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupVideoDownload(VideoInfo info)
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp4", It.IsAny<string?>())).ReturnsAsync(@"C:\out\clip.mp4");
    }

    private void VerifyNoMux()
    {
        _converter.Verify(c => c.MuxToMp4Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _converter.Verify(c => c.MuxToMp4CopyAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Download_VideoMode_StartsBothStreamsBeforeEitherCompletes()
    {
        SetupVideoDownload(SampleInfo());
        // Each stream blocks on its own gate; record which downloads have started.
        var videoGate = new TaskCompletionSource();
        var audioGate = new TaskCompletionSource();
        var started = new List<string>();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string filePath, IProgress<double>? _, CancellationToken ct) =>
                {
                    lock (started) started.Add(filePath);
                    return (filePath == @"C:\temp\v.mp4" ? videoGate : audioGate).Task.WaitAsync(ct);
                });

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        var download = vm.DownloadCommand.ExecuteAsync(null);

        // Sequential code would block on the video gate and never start the audio download.
        Assert.True(SpinWait.SpinUntil(() => { lock (started) return started.Count == 2; }, TimeSpan.FromSeconds(5)));
        lock (started)
        {
            Assert.Contains(@"C:\temp\v.mp4", started);
            Assert.Contains(@"C:\temp\a.webm", started);
        }
        Assert.False(download.IsCompleted);

        videoGate.SetResult();
        audioGate.SetResult();
        await download.WaitAsync(TimeSpan.FromSeconds(5));

        // SampleInfo() has no AAC stream, so plan 008's re-encode fallback mux is used.
        _converter.Verify(c => c.MuxToMp4Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1.0, vm.Progress);
    }

    [Fact]
    public async Task Download_VideoMode_WhenAudioFails_CancelsVideoAndReportsFailure()
    {
        SetupVideoDownload(SampleInfo());
        // The video download runs until cancelled; the audio download fails immediately.
        var videoToken = CancellationToken.None;
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string filePath, IProgress<double>? _, CancellationToken ct) =>
                {
                    if (filePath == @"C:\temp\v.mp4")
                    {
                        videoToken = ct;
                        return Task.Delay(Timeout.Infinite, ct);
                    }
                    return Task.FromException(new InvalidOperationException("audio boom"));
                });

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        await vm.DownloadCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(videoToken.IsCancellationRequested);
        Assert.Equal("Download failed: audio boom", vm.StatusMessage);
        Assert.Empty(vm.History);
        Assert.False(vm.IsDownloading);
        VerifyNoMux();
    }

    [Fact]
    public async Task Download_VideoMode_UserCancelDuringParallelStreams_ReportsCancelled()
    {
        SetupVideoDownload(SampleInfo());
        // Both downloads block on one gate that never opens: only a cancel ends them.
        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        await vm.CancelDownloadCommand.ExecuteAsync(null);
        await download.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.False(vm.IsDownloading);
        VerifyNoMux();
    }

    [Fact]
    public async Task Download_AudioMode_RecordsHistoryEntry()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedQuality = Mp3Quality.Cbr256;

        await vm.DownloadCommand.ExecuteAsync(null);

        var entry = Assert.Single(vm.History);
        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", entry.Url);
        Assert.Equal("Test Video", entry.Title);
        Assert.Equal("MP3 · 256 kbps", entry.Format);
        Assert.Equal(@"C:\out\song.mp3", entry.FilePath);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);
    }

    [Fact]
    public async Task Download_Failure_DoesNotRecordHistory()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
        _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg boom"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Empty(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Never);
    }

    [Fact]
    public async Task Download_HistorySaveFails_KeepsEntryAndReportsDone()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
        _history.Setup(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>())).Throws(new IOException("disk full"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Single(vm.History);
        Assert.StartsWith("Done! Saved to", vm.StatusMessage);
        Assert.Contains("disk full", vm.StatusMessage);
        Assert.DoesNotContain("Download failed", vm.StatusMessage);
    }

    [Fact]
    public async Task Download_SettingsSaveFails_StillDownloads()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
        _settings.Setup(s => s.Save(It.IsAny<AppSettings>())).Throws(new IOException("read-only"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(vm.History);
    }

    [Fact]
    public async Task Download_WhenFfmpegMissing_ShowsFirstRunStatus()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.IsAvailable).Returns(false);
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        string? statusDuringEnsure = null;
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .Callback(() => statusDuringEnsure = vm.StatusMessage)
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedQuality = Mp3Quality.Cbr256;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.NotNull(statusDuringEnsure);
        Assert.Contains("first run", statusDuringEnsure);
    }

    [Fact]
    public void Constructor_LoadsExistingHistory()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = "u1", Title = "t1", Format = "MP3 · 192 kbps" },
            new() { Url = "u2", Title = "t2", Format = "MP4 · 720p" },
        });

        var vm = CreateSut();

        Assert.Equal(2, vm.History.Count);
        Assert.Equal("u1", vm.History[0].Url);
    }

    [Fact]
    public async Task UseEntry_SetsUrlSwitchesTabAndFetches()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        var vm = CreateSut();
        vm.SelectedTabIndex = 1;

        await vm.UseEntryCommand.ExecuteAsync(new HistoryEntry { Url = "https://youtu.be/dQw4w9WgXcQ" });

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", vm.Url);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.Same(info, vm.VideoInfo);
        _youtube.Verify(s => s.GetVideoInfoAsync("https://youtu.be/dQw4w9WgXcQ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UseEntry_CannotExecute_WhileBusy()
    {
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(vm, true);

        Assert.False(vm.UseEntryCommand.CanExecute(new HistoryEntry { Url = "https://youtu.be/dQw4w9WgXcQ" }));
    }

    [Fact]
    public async Task Url_ChangedToDifferentVideo_ClearsVideoInfoAndDisablesDownload()
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(SampleInfo());
        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        vm.Url = "https://youtu.be/oHg5SJYRHA0";

        Assert.Null(vm.VideoInfo);
        Assert.Null(vm.SelectedVideoQuality);
        Assert.False(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Url_ChangedToSameVideoDifferentForm_KeepsVideoInfo()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        vm.Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s";

        Assert.Same(info, vm.VideoInfo);
        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    private void SetupMp3Download(VideoInfo info)
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
    }

    [Fact]
    public async Task Download_ReportArrivingAfterTheOutcome_DoesNotOverwriteTheFinalStatus()
    {
        SetupMp3Download(SampleInfo());
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        IProgress<PipelineProgress>? captured = null;
        var pipeline = new Mock<IDownloadPipeline>();
        pipeline.Setup(p => p.RunAsync(It.IsAny<DownloadRequest>(), It.IsAny<IProgress<PipelineProgress>>(), It.IsAny<CancellationToken>()))
                .Callback<DownloadRequest, IProgress<PipelineProgress>, CancellationToken>((_, progress, _) => captured = progress)
                .ReturnsAsync(new DownloadOutcome.Cancelled());
        var vm = new MainViewModel(_youtube.Object, _ffmpeg.Object, pipeline.Object,
            _saveFile.Object, _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object,
            _clipboard.Object, _confirm.Object, _foobar.Object);
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);

        captured!.Report(new PipelineProgress(PipelineStage.Converting, 0.9));

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.Equal(0, vm.Progress);
    }

    [Fact]
    public async Task Download_AfterUrlEditedToSameVideo_RecordsFetchedUrl()
    {
        SetupMp3Download(SampleInfo(sourceUrl: "https://youtu.be/dQw4w9WgXcQ"));
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s";

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", Assert.Single(vm.History).Url);
    }

    [Fact]
    public async Task Download_UrlChangedMidFlight_RecordsFetchedUrlAndFinishes()
    {
        SetupMp3Download(SampleInfo());
        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe")
               .Callback(() => vm.Url = "https://youtu.be/oHg5SJYRHA0");

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", Assert.Single(vm.History).Url);
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Download_VideoQualityChangedMidFlight_UsesQualityCapturedAtStart()
    {
        var q1080 = new VideoStreamOption("1080p", 1080, "mp4", Source: null!);
        var q720 = new VideoStreamOption("720p", 720, "mp4", Source: null!);
        var info = new VideoInfo(
            SourceUrl: "https://youtu.be/dQw4w9WgXcQ",
            Title: "Test Video",
            Author: "Tester",
            Duration: TimeSpan.FromMinutes(3),
            ThumbnailUrl: "",
            BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
            VideoOptions: new[] { q1080, q720 });
        SetupMp3Download(info);
        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;
        vm.SelectedVideoQuality = q720;
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe")
               .Callback(() => vm.SelectedVideoQuality = null);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("MP4 · 720p", Assert.Single(vm.History).Format);
    }

    [Fact]
    public void OpenInBrowser_CallsLinkOpener()
    {
        var vm = CreateSut();

        vm.OpenInBrowserCommand.Execute(new HistoryEntry { Url = "https://youtu.be/zzz" });

        _linkOpener.Verify(o => o.Open("https://youtu.be/zzz"), Times.Once);
    }

    [Fact]
    public void RemoveEntry_RemovesAndSaves()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = "u1" }, new() { Url = "u2" },
        });
        var vm = CreateSut();
        var first = vm.History[0];

        vm.RemoveEntryCommand.Execute(first);

        Assert.Single(vm.History);
        Assert.DoesNotContain(first, vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);
    }

    [Fact]
    public async Task ClearHistory_WhenConfirmed_EmptiesAndSaves()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);
    }

    [Fact]
    public void RemoveEntry_WhenSaveThrows_DoesNotThrowAndReportsStatus()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = "u1" }, new() { Url = "u2" },
        });
        _history.Setup(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>())).Throws(new IOException("locked"));
        var vm = CreateSut();
        var first = vm.History[0];

        var ex = Record.Exception(() => vm.RemoveEntryCommand.Execute(first));

        Assert.Null(ex);
        Assert.Single(vm.History);
        Assert.DoesNotContain(first, vm.History);
        Assert.Contains("Couldn't save history", vm.StatusMessage);
    }

    [Fact]
    public async Task ClearHistory_WhenSaveThrows_DoesNotThrowAndReportsStatus()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        _history.Setup(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>())).Throws(new IOException("locked"));
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var vm = CreateSut();

        var ex = await Record.ExceptionAsync(() => vm.ClearHistoryCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.Empty(vm.History);
        Assert.Contains("Couldn't save history", vm.StatusMessage);
    }

    [Fact]
    public async Task ClearHistory_WhenDeclined_KeepsHistory()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Single(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Never);
    }

    [Fact]
    public async Task ClearHistory_WhenEmpty_DoesNotPrompt()
    {
        var vm = CreateSut();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        _confirm.Verify(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void HistoryGroups_BucketByDate_TodayAndYesterday()
    {
        var now = DateTimeOffset.Now;
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = "a", Title = "Alpha", DownloadedAt = now },
            new() { Url = "b", Title = "Beta",  DownloadedAt = now.AddDays(-1) },
            new() { Url = "c", Title = "Gamma", DownloadedAt = now.AddDays(-1) },
        });

        var vm = CreateSut();

        Assert.Equal(2, vm.HistoryGroups.Count);
        Assert.Equal("Today", vm.HistoryGroups[0].Header);
        Assert.Single(vm.HistoryGroups[0].Entries);
        Assert.Equal("Yesterday", vm.HistoryGroups[1].Header);
        Assert.Equal(2, vm.HistoryGroups[1].Entries.Count);
    }

    [Fact]
    public void SearchText_FiltersByTitleAndUrl_AndSetsPlaceholder()
    {
        var now = DateTimeOffset.Now;
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = "https://y/1",    Title = "Lofi beats",  DownloadedAt = now },
            new() { Url = "https://y/rick", Title = "Never Gonna", DownloadedAt = now },
        });
        var vm = CreateSut();
        Assert.Equal(2, vm.HistoryGroups.Sum(g => g.Entries.Count));
        Assert.Null(vm.HistoryPlaceholder);

        vm.SearchText = "lofi"; // matches title (case-insensitive)
        Assert.Equal(1, vm.HistoryGroups.Sum(g => g.Entries.Count));
        Assert.Equal("Lofi beats", vm.HistoryGroups[0].Entries[0].Title);

        vm.SearchText = "rick"; // matches URL
        Assert.Equal(1, vm.HistoryGroups.Sum(g => g.Entries.Count));

        vm.SearchText = "nothing-here";
        Assert.Empty(vm.HistoryGroups);
        Assert.NotNull(vm.HistoryPlaceholder);
    }

    [Fact]
    public void OpenFolder_WithPath_RevealsFile()
    {
        var vm = CreateSut();

        vm.OpenFolderCommand.Execute(new HistoryEntry { FilePath = @"C:\out\song.mp3" });

        _fileRevealer.Verify(r => r.RevealInFolder(@"C:\out\song.mp3"), Times.Once);
    }

    [Fact]
    public void OpenFolder_WithoutPath_DoesNothing()
    {
        var vm = CreateSut();

        vm.OpenFolderCommand.Execute(new HistoryEntry { FilePath = "" });

        _fileRevealer.Verify(r => r.RevealInFolder(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OpenInFoobar_WhenInstalledWithFile_LaunchesIt()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = @"C:\out\song.opus" });

        _foobar.Verify(f => f.Open(@"C:\out\song.opus"), Times.Once);
    }

    [Fact]
    public void OpenInFoobar_WhenNotInstalled_DoesNotLaunch()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(false);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = @"C:\out\song.opus" });

        _foobar.Verify(f => f.Open(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OpenInFoobar_NullEntryOrNoFilePath_DoesNotLaunch()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        var vm = CreateSut();

        vm.OpenInFoobarCommand.Execute(null);
        vm.OpenInFoobarCommand.Execute(new HistoryEntry { FilePath = "" });

        _foobar.Verify(f => f.Open(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void FoobarTooltip_ReflectsInstalledState()
    {
        _foobar.SetupGet(f => f.IsInstalled).Returns(false);
        Assert.Equal("foobar2000 is not installed", CreateSut().FoobarTooltip);

        _foobar.SetupGet(f => f.IsInstalled).Returns(true);
        Assert.Equal("Open in foobar2000", CreateSut().FoobarTooltip);
    }

    [Fact]
    public void AlwaysOnTop_DefaultsToTrue()
    {
        Assert.True(CreateSut().AlwaysOnTop);
    }

    [Fact]
    public void Constructor_LoadsAlwaysOnTopFromSettings()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { AlwaysOnTop = false });

        Assert.False(CreateSut().AlwaysOnTop);
    }

    [Fact]
    public void AlwaysOnTop_WhenChanged_PersistsSetting()
    {
        var vm = CreateSut(); // loaded default (true), no save during construction

        vm.AlwaysOnTop = false;

        _settings.Verify(s => s.Save(It.Is<AppSettings>(a => a.AlwaysOnTop == false)), Times.Once);
    }

    [Fact]
    public void AlwaysOnTop_WhenSaveThrows_DoesNotThrowAndReportsStatus()
    {
        _settings.Setup(s => s.Save(It.IsAny<AppSettings>())).Throws(new IOException("read-only"));
        var vm = CreateSut();

        var ex = Record.Exception(() => vm.AlwaysOnTop = false);

        Assert.Null(ex);
        Assert.Contains("Couldn't save settings", vm.StatusMessage);
    }

    [Fact]
    public void Constructor_RestoresLastModeAndQuality()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "Video", LastMp3Quality = "cbr-192" });

        var vm = CreateSut();

        Assert.Equal(DownloadMode.Video, vm.SelectedMode);
        Assert.Same(Mp3Quality.Cbr192, vm.SelectedQuality);
    }

    [Fact]
    public void Constructor_UnknownSavedValues_FallBackToDefaults()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "Flac", LastMp3Quality = "nope" });

        var vm = CreateSut();

        Assert.Equal(DownloadMode.AudioMp3, vm.SelectedMode);
        Assert.Same(Mp3Quality.BestVbr, vm.SelectedQuality);
    }

    [Fact]
    public void Constructor_WithSavedValues_DoesNotSave()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "AudioOpus", LastMp3Quality = "cbr-320" });

        CreateSut();

        _settings.Verify(s => s.Save(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public void SelectedMode_WhenChanged_PersistsName()
    {
        var vm = CreateSut();

        vm.SelectedMode = DownloadMode.AudioOpus;

        _settings.Verify(s => s.Save(It.Is<AppSettings>(a => a.LastMode == "AudioOpus")), Times.AtLeastOnce());
    }

    [Fact]
    public void SelectedQuality_WhenChanged_PersistsKey()
    {
        var vm = CreateSut();

        vm.SelectedQuality = Mp3Quality.Cbr256;

        _settings.Verify(s => s.Save(It.Is<AppSettings>(a => a.LastMp3Quality == "cbr-256")), Times.AtLeastOnce());
    }

    [Fact]
    public void SelectedQuality_SetToNull_DoesNotThrowOrSave()
    {
        var vm = CreateSut();

        var ex = Record.Exception(() => vm.SelectedQuality = null!);

        Assert.Null(ex);
        _settings.Verify(s => s.Save(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task Download_PassesRememberedFolderToSaveDialog()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastSaveFolder = @"C:\Music" });
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", @"C:\Music")).ReturnsAsync(@"C:\Music\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);

        _saveFile.Verify(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", @"C:\Music"), Times.Once);
    }

    [Fact]
    public async Task Download_UpdatesLastSaveFolderToChosenLocation()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);

        _settings.Verify(s => s.Save(It.Is<AppSettings>(a => a.LastSaveFolder == @"C:\out")), Times.Once);
    }

    [Fact]
    public void CheckClipboard_ValidNewLink_SetsSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_NonYouTubeText_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("just some text");
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_EmptyClipboard_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns((string?)null);
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_LinkSameVideoAsUrlBox_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"; // same video, different URL form

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_WhileBusy_NoSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(vm, true);

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void DismissDetectedLink_HidesSuggestion()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        Assert.NotNull(vm.DetectedClipboardUrl);

        vm.DismissDetectedLinkCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_AfterDismiss_SameLinkStaysSuppressed()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        vm.DismissDetectedLinkCommand.Execute(null);

        vm.CheckClipboardCommand.Execute(null); // same link still on clipboard

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CheckClipboard_AfterDismiss_DifferentLinkReappears()
    {
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        vm.DismissDetectedLinkCommand.Execute(null);

        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/oHg5SJYRHA0");
        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal("https://youtu.be/oHg5SJYRHA0", vm.DetectedClipboardUrl);
    }

    [Fact]
    public async Task UseDetectedLink_SetsUrlClearsBannerAndFetches()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);

        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", vm.Url);
        Assert.Null(vm.DetectedClipboardUrl);
        Assert.Same(info, vm.VideoInfo);
        _youtube.Verify(s => s.GetVideoInfoAsync("https://youtu.be/dQw4w9WgXcQ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseDetectedLink_NoSuggestion_DoesNothing()
    {
        var vm = CreateSut(); // DetectedClipboardUrl is null

        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Url);
        _youtube.Verify(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseDetectedLink_ThenCheckClipboard_DoesNotReappear()
    {
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(SampleInfo());
        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.CheckClipboardCommand.Execute(null);
        await vm.UseDetectedLinkCommand.ExecuteAsync(null);

        vm.CheckClipboardCommand.Execute(null); // same link still on clipboard

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public void CancelDownload_CannotExecute_WhenNotDownloading()
    {
        var vm = CreateSut();

        Assert.False(vm.IsDownloading);
        Assert.False(vm.CancelDownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Download_Success_LeavesIsDownloadingFalse()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task Download_ModeChangedMidFlight_UsesModeCapturedAtStart()
    {
        var info = SampleInfo(withOpus: false); // no Opus stream available
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        // Simulate the user flipping FORMAT to Opus mid-download (during the ffmpeg bootstrap await).
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe")
               .Callback(() => vm.SelectedMode = DownloadMode.AudioOpus);

        await vm.DownloadCommand.ExecuteAsync(null);

        // Must honor the mode captured at start (MP3), not the flipped-to Opus (which would NPE on the null BestOpusAudio).
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.RemuxToOpusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<MediaTags?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ShowQuality_OnlyInMp3Mode()
    {
        var vm = CreateSut();

        vm.SelectedMode = DownloadMode.AudioMp3;
        Assert.True(vm.ShowQuality);
        vm.SelectedMode = DownloadMode.Video;
        Assert.False(vm.ShowQuality);
        vm.SelectedMode = DownloadMode.AudioOpus;
        Assert.False(vm.ShowQuality);
    }

    [Fact]
    public void ShowVideoQuality_OnlyInVideoMode()
    {
        var vm = CreateSut();

        vm.SelectedMode = DownloadMode.Video;
        Assert.True(vm.ShowVideoQuality);
        vm.SelectedMode = DownloadMode.AudioMp3;
        Assert.False(vm.ShowVideoQuality);
        vm.SelectedMode = DownloadMode.AudioOpus;
        Assert.False(vm.ShowVideoQuality);
    }

    [Fact]
    public void SelectedMode_Change_RaisesBothPickerVisibilities()
    {
        var vm = CreateSut();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedMode = DownloadMode.Video;

        Assert.Contains(nameof(MainViewModel.ShowQuality), raised);
        Assert.Contains(nameof(MainViewModel.ShowVideoQuality), raised);
    }

    [Fact]
    public void SelectedQuality_DefaultsToBestVbr()
    {
        Assert.Equal(Mp3Quality.BestVbr, CreateSut().SelectedQuality);
    }

    [Fact]
    public async Task Download_DefaultVbr_ConvertsWithVbrAndRecordsVbrLabel()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        // SelectedQuality defaults to BestVbr — do not set it.

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.ConvertToMp3Async(
            @"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.part.mp3", Mp3Quality.BestVbr, It.IsAny<MediaTags?>(), It.IsAny<string?>(),
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("MP3 · VBR V0 (~245 kbps)", Assert.Single(vm.History).Format);
    }

    [Fact]
    public async Task Download_OpusMode_RemuxesNativeStreamAndRecordsOpusFormat()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "opus", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.opus");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioOpus;

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.RemuxToOpusAsync(@"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.part.opus", It.IsAny<MediaTags?>(),
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Opus · 160 kbps", Assert.Single(vm.History).Format);
    }

    [Fact]
    public async Task Download_OpusMode_NoOpusStream_ErrorsWithoutPrompting()
    {
        var info = SampleInfo(withOpus: false);
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioOpus;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Contains("Opus", vm.StatusMessage);
        _saveFile.Verify(s => s.PromptForSavePathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Empty(vm.History);
    }

    [Fact]
    public async Task CancelDownload_WhenUserConfirms_CancelsAndAsksForConfirmation()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        // The audio download blocks until the token is cancelled.
        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        // EnsureAsync completes synchronously via Moq, so IsDownloading is already true here.
        Assert.True(vm.IsDownloading);

        await vm.CancelDownloadCommand.ExecuteAsync(null);
        await download;

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.False(vm.IsDownloading);
        _confirm.Verify(c => c.ConfirmAsync(
            "Cancel the current download? Your progress will be lost.",
            "Cancel download?"), Times.Once);
    }

    [Fact]
    public async Task CancelDownload_WhenUserDeclines_LetsDownloadContinue()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);

        // User declines the cancel: the download must keep running.
        await vm.CancelDownloadCommand.ExecuteAsync(null);
        Assert.NotEqual("Download cancelled.", vm.StatusMessage);
        Assert.True(vm.IsDownloading);
        _confirm.Verify(c => c.ConfirmAsync(
            "Cancel the current download? Your progress will be lost.",
            "Cancel download?"), Times.Once);

        // Let the (still-running) download finish normally.
        gate.SetResult();
        await download;

        Assert.Equal(@"C:\out\song.mp3", Assert.Single(vm.History).FilePath);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task Download_Cancelled_KeepsExistingTargetAndDiscardsPartFile()
    {
        // A real pre-existing file the user chose to overwrite: a cancelled run must leave it untouched.
        var target = Path.Combine(Path.GetTempPath(), $"cancel-test-{Guid.NewGuid():N}.mp3");
        File.WriteAllText(target, "original bytes");

        try
        {
            var info = SampleInfo();
            _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
            _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
            _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
            _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(target);
            // Block the audio download until the token is cancelled, then throw.
            _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                        It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                    .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) =>
                        Task.Delay(Timeout.Infinite, ct));

            var vm = CreateSut();
            vm.Url = "https://youtu.be/dQw4w9WgXcQ";
            await vm.FetchInfoCommand.ExecuteAsync(null);
            vm.SelectedMode = DownloadMode.AudioMp3;

            // IsDownloading is set before the first awaited call that actually yields
            // (EnsureAsync completes synchronously via Moq), so it is already true here.
            var download = vm.DownloadCommand.ExecuteAsync(null);
            Assert.True(vm.IsDownloading);
            Assert.True(vm.CancelDownloadCommand.CanExecute(null));

            vm.CancelDownloadCommand.Execute(null);
            await download;

            Assert.Equal("Download cancelled.", vm.StatusMessage);
            Assert.Equal(0, vm.Progress);
            Assert.Empty(vm.History);
            Assert.False(vm.IsDownloading);
            Assert.Equal("original bytes", File.ReadAllText(target));
            _output.Verify(o => o.Discard(PartFilePath.For(target)), Times.Once);
            _output.Verify(o => o.Commit(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }

    [Fact]
    public async Task Download_Success_CommitsPartToTargetAndRecordsTarget()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        _output.Verify(o => o.Commit(@"C:\out\song.part.mp3", @"C:\out\song.mp3"), Times.Once);
        _output.Verify(o => o.Discard(It.IsAny<string>()), Times.Never);
        Assert.Equal(@"C:\out\song.mp3", Assert.Single(vm.History).FilePath);
    }

    [Fact]
    public async Task Download_Failure_DiscardsPartAndNeverCommits()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
        _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg boom"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        _output.Verify(o => o.Discard(@"C:\out\song.part.mp3"), Times.Once);
        _output.Verify(o => o.Commit(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.StartsWith("Download failed", vm.StatusMessage);
    }

    [Fact]
    public async Task Download_CommitFails_ReportsFailureAndRecordsNoHistory()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), "mp3", It.IsAny<string?>())).ReturnsAsync(@"C:\out\song.mp3");
        _output.Setup(o => o.Commit(It.IsAny<string>(), It.IsAny<string>())).Throws(new IOException("locked"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Contains("locked", vm.StatusMessage);
        Assert.Empty(vm.History);
        _output.Verify(o => o.Discard(@"C:\out\song.part.mp3"), Times.Once);
    }

    private const string CoverArtUrl = "https://i.ytimg.com/vi_webp/dQw4w9WgXcQ/maxresdefault.webp";

    private async Task<MainViewModel> FetchAndDownloadAsync(VideoInfo info, DownloadMode mode)
    {
        var ext = mode == DownloadMode.AudioOpus ? "opus" : "mp3";
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePathAsync(It.IsAny<string>(), ext, It.IsAny<string?>())).ReturnsAsync($@"C:\out\song.{ext}");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = mode;

        await vm.DownloadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Download_Mp3_PassesTagsAndCoverToConverter()
    {
        var info = SampleInfo() with { UploadDate = new DateTimeOffset(2009, 10, 25, 0, 0, 0, TimeSpan.Zero), CoverArtUrl = CoverArtUrl };
        _thumbnails.Setup(t => t.TryDownloadAsync(CoverArtUrl, It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\temp\cover.jpg");

        var vm = await FetchAndDownloadAsync(info, DownloadMode.AudioMp3);

        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.Is<MediaTags?>(t => t!.Title == "Test Video" && t.Artist == "Tester"
                && t.SourceUrl == "https://youtu.be/dQw4w9WgXcQ" && t.Year == 2009),
            @"C:\temp\cover.jpg",
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(vm.History);
    }

    [Fact]
    public async Task Download_Mp3_NoCoverAvailable_ConvertsWithNullCover()
    {
        _thumbnails.Setup(t => t.TryDownloadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var vm = await FetchAndDownloadAsync(SampleInfo(), DownloadMode.AudioMp3);

        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.Is<MediaTags?>(t => t != null && t.Year == null), null,
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(vm.History);
    }

    [Fact]
    public async Task Download_Mp3_ConversionWithCoverFails_RetriesWithoutCover()
    {
        _thumbnails.Setup(t => t.TryDownloadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\temp\cover.jpg");
        _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
                It.IsAny<MediaTags?>(), @"C:\temp\cover.jpg",
                It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("FFmpeg exited with code 1: bad cover"));

        var vm = await FetchAndDownloadAsync(SampleInfo(), DownloadMode.AudioMp3);

        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.IsAny<MediaTags?>(), @"C:\temp\cover.jpg",
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.Is<MediaTags?>(t => t != null), null,
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(vm.History);
        Assert.StartsWith("Done!", vm.StatusMessage);
    }

    [Fact]
    public async Task Download_Opus_PassesTags()
    {
        var info = SampleInfo() with { UploadDate = new DateTimeOffset(2009, 10, 25, 0, 0, 0, TimeSpan.Zero), CoverArtUrl = CoverArtUrl };

        var vm = await FetchAndDownloadAsync(info, DownloadMode.AudioOpus);

        _converter.Verify(c => c.RemuxToOpusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<MediaTags?>(t => t!.Title == "Test Video" && t.Artist == "Tester"
                && t.SourceUrl == "https://youtu.be/dQw4w9WgXcQ" && t.Year == 2009),
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _thumbnails.Verify(t => t.TryDownloadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never); // no Opus cover
        Assert.Single(vm.History);
    }
}
