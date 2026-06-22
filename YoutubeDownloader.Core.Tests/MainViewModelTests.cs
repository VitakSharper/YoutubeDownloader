using Moq;
using YoutubeExplode.Videos.Streams;
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

    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _converter.Object, _saveFile.Object, _temp.Object);

    private static VideoInfo SampleInfo(int sourceAudioKbps = 128) => new(
        Title: "Test Video",
        Author: "Tester",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", sourceAudioKbps, "webm", Source: null!),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Source: null!) });

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
    public void UpscaleWarning_ShownWhenBitrateExceedsSource_InAudioMode()
    {
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.VideoInfo))!
            .SetValue(vm, SampleInfo(sourceAudioKbps: 128));
        vm.SelectedMode = DownloadMode.AudioMp3;

        vm.SelectedBitrate = Mp3Bitrate.Kbps320;
        Assert.NotNull(vm.UpscaleWarning);

        vm.SelectedBitrate = Mp3Bitrate.Kbps128;
        Assert.Null(vm.UpscaleWarning);
    }

    [Fact]
    public void UpscaleWarning_NeverShownInVideoMode()
    {
        var vm = CreateSut();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.VideoInfo))!
            .SetValue(vm, SampleInfo(sourceAudioKbps: 128));
        vm.SelectedBitrate = Mp3Bitrate.Kbps320;
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
    public async Task Download_AudioMode_ConvertsWithSelectedBitrate()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3")).Returns(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;
        vm.SelectedBitrate = Mp3Bitrate.Kbps256;

        await vm.DownloadCommand.ExecuteAsync(null);

        _converter.Verify(c => c.ConvertToMp3Async(
            @"C:\ffmpeg\ffmpeg.exe", @"C:\temp\a.webm", @"C:\out\song.mp3", 256,
            info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Download_UserCancelsSaveDialog_DoesNothing()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), It.IsAny<string>())).Returns((string?)null);

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp4")).Returns(@"C:\out\clip.mp4");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.Video;

        await vm.DownloadCommand.ExecuteAsync(null);

        _youtube.Verify(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
            It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _converter.Verify(c => c.MuxToMp4Async(@"C:\ffmpeg\ffmpeg.exe", @"C:\temp\v.mp4", @"C:\temp\a.webm",
            @"C:\out\clip.mp4", info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
