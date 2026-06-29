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
    private readonly Mock<IHistoryStore> _history = new();
    private readonly Mock<ILinkOpener> _linkOpener = new();
    private readonly Mock<IFileRevealer> _fileRevealer = new();
    private readonly Mock<ISettingsStore> _settings = new();
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<IConfirmationService> _confirm = new();

    public MainViewModelTests()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>());
        _settings.Setup(s => s.Load()).Returns(new AppSettings());
        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
    }

    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _converter.Object, _saveFile.Object, _temp.Object,
            _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object, _clipboard.Object,
            _confirm.Object);

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns((string?)null);

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp4", It.IsAny<string?>())).Returns(@"C:\out\clip.mp4");

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

    [Fact]
    public async Task Download_AudioMode_RecordsHistoryEntry()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedBitrate = Mp3Bitrate.Kbps256;

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");
        _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ffmpeg boom"));

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Empty(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Never);
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
    public void UseEntry_SetsUrlAndSwitchesToDownloadTab()
    {
        var vm = CreateSut();
        vm.SelectedTabIndex = 1;

        vm.UseEntryCommand.Execute(new HistoryEntry { Url = "https://youtu.be/zzz" });

        Assert.Equal("https://youtu.be/zzz", vm.Url);
        Assert.Equal(0, vm.SelectedTabIndex);
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
    public void ClearHistory_EmptiesAndSaves()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry> { new() { Url = "u1" } });
        var vm = CreateSut();

        vm.ClearHistoryCommand.Execute(null);

        Assert.Empty(vm.History);
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);
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
    public async Task Download_PassesRememberedFolderToSaveDialog()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastSaveFolder = @"C:\Music" });
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", @"C:\Music")).Returns(@"C:\Music\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);

        _saveFile.Verify(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", @"C:\Music"), Times.Once);
    }

    [Fact]
    public async Task Download_UpdatesLastSaveFolderToChosenLocation()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

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
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task CancelDownload_WhenUserConfirms_CancelsAndAsksForConfirmation()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        // The audio download blocks until the token is cancelled.
        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);

        vm.CancelDownloadCommand.Execute(null);
        await download;

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        Assert.False(vm.IsDownloading);
        _confirm.Verify(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelDownload_WhenUserDeclines_LetsDownloadContinue()
    {
        var info = SampleInfo();
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(@"C:\out\song.mp3");

        var gate = new TaskCompletionSource();
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => gate.Task.WaitAsync(ct));

        _confirm.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var vm = CreateSut();
        vm.Url = "https://youtu.be/dQw4w9WgXcQ";
        await vm.FetchInfoCommand.ExecuteAsync(null);
        vm.SelectedMode = DownloadMode.AudioMp3;

        var download = vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloading);

        // User declines the cancel: the download must keep running.
        vm.CancelDownloadCommand.Execute(null);
        Assert.NotEqual("Download cancelled.", vm.StatusMessage);
        Assert.True(vm.IsDownloading);
        _confirm.Verify(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        // Let the (still-running) download finish normally.
        gate.SetResult();
        await download;

        Assert.Equal(@"C:\out\song.mp3", Assert.Single(vm.History).FilePath);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task Download_Cancelled_SetsCancelledStatusResetsProgressNoHistoryAndDeletesPartialFile()
    {
        // A real file on disk stands in for the partially-written output.
        var target = Path.Combine(Path.GetTempPath(), $"cancel-test-{Guid.NewGuid():N}.mp3");
        File.WriteAllText(target, "partial bytes");

        try
        {
            var info = SampleInfo();
            _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
            _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(@"C:\ffmpeg\ffmpeg.exe");
            _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
            _saveFile.Setup(s => s.PromptForSavePath(It.IsAny<string>(), "mp3", It.IsAny<string?>())).Returns(target);
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
            Assert.False(File.Exists(target));
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
