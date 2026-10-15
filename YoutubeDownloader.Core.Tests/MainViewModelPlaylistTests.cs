using Moq;
using YoutubeExplode.Exceptions;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Core.Tests;

/// <summary>Playlist links on the Download tab (plan 026a): routing, the choice banner, the playlist card, the clipboard banner.</summary>
public class MainViewModelPlaylistTests
{
    private const string ListId = "PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e";
    private const string PlaylistUrl = "https://www.youtube.com/playlist?list=" + ListId;
    private const string WatchInList = "https://www.youtube.com/watch?v=uPZSSdkGQhM&list=" + ListId + "&index=1";
    private const string WatchUrl = "https://www.youtube.com/watch?v=uPZSSdkGQhM";
    private const string MixPlaylistUrl = "https://www.youtube.com/playlist?list=RDTsYhxMnGYCw";

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
    private readonly Mock<IFolderPickerService> _folderPicker = new();
    private readonly Mock<IRetryDelay> _retryDelay = Mock.Get(Mock.Of<IRetryDelay>(d =>
        d.WaitAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()) == Task.CompletedTask));

    public MainViewModelPlaylistTests()
    {
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>());
        _settings.Setup(s => s.Load()).Returns(new AppSettings());
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
    }

    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object,
            new DownloadPipeline(_youtube.Object, _converter.Object, _temp.Object, _output.Object, _thumbnails.Object),
            _saveFile.Object, _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object,
            _clipboard.Object, _confirm.Object, _foobar.Object, _folderPicker.Object, _retryDelay.Object);

    private static VideoInfo SampleInfo(string sourceUrl) => new(
        SourceUrl: sourceUrl,
        Title: "Test Video",
        Author: "Tester",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Source: null!) },
        BestOpusAudio: new AudioStreamOption("opus", 160, "webm", Source: null!),
        BestMp4Audio: null);

    private static QueueItem Item(int i, string? title = null) =>
        new($"vid{i:D8}", PlaylistMapping.WatchUrl($"vid{i:D8}"), title ?? $"Title {i}", "Channel", TimeSpan.FromMinutes(3), i, null);

    private static IReadOnlyList<QueueItem> Items(int count) => Enumerable.Range(1, count).Select(i => Item(i)).ToList();

    private void SetupPlaylist(int? count, params IReadOnlyList<QueueItem>[] batches)
    {
        _youtube.Setup(y => y.GetPlaylistAsync(ListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaylistInfo(ListId, "Pharmacology", "Armando Hasudungan", count, null));
        _youtube.Setup(y => y.GetPlaylistItemBatchesAsync(ListId, It.IsAny<CancellationToken>()))
            .Returns(() => Batches(batches));
    }

    private void SetupVideo(string sourceUrl) =>
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(SampleInfo(sourceUrl));

    private static async IAsyncEnumerable<IReadOnlyList<QueueItem>> Batches(IReadOnlyList<QueueItem>[] batches)
    {
        foreach (var batch in batches)
            yield return batch;
        await Task.CompletedTask;   // keeps the compiler quiet about an async iterator without await
    }

    private async Task<MainViewModel> LoadedPlaylistAsync()
    {
        SetupPlaylist(3, [Item(1), Item(2)], [Item(3)]);
        var vm = CreateSut();
        vm.Url = PlaylistUrl;
        await vm.FetchInfoCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Playlist);
        return vm;
    }

    [Fact]
    public async Task FetchInfo_PlaylistUrl_LoadsTheCardWithEveryBatch()
    {
        var vm = await LoadedPlaylistAsync();

        Assert.Equal([1, 2, 3], vm.Playlist!.Items.Select(row => row.Item.Index));
        Assert.Equal("Pharmacology", vm.Playlist.Title);
        Assert.False(vm.Playlist.IsLoading);
        Assert.Null(vm.VideoInfo);
        Assert.True(vm.IsPlaylistMode);
        Assert.False(vm.ShowSingleDownload);
        Assert.False(vm.IsBusy);
        Assert.Equal("Loaded: Pharmacology · 3 videos", vm.StatusMessage);
        _youtube.Verify(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FetchInfo_WatchLinkWithList_FetchesTheVideoByWatchUrlAndOffersTheChoice()
    {
        SetupVideo(WatchUrl);
        var vm = CreateSut();
        vm.Url = WatchInList;

        await vm.FetchInfoCommand.ExecuteAsync(null);

        _youtube.Verify(y => y.GetVideoInfoAsync(WatchUrl, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(vm.VideoInfo);
        Assert.True(vm.HasPlaylistChoice);
        Assert.Equal("This link is part of a playlist.", vm.PlaylistChoiceText);
        Assert.Null(vm.Playlist);
        Assert.True(vm.ShowSingleDownload);
    }

    [Fact]
    public async Task ChooseWholePlaylist_LoadsThePlaylistAndHidesTheChoice()
    {
        SetupVideo(WatchUrl);
        SetupPlaylist(2, [Item(1), Item(2)]);
        var vm = CreateSut();
        vm.Url = WatchInList;
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.ChooseWholePlaylistCommand.ExecuteAsync(null);

        Assert.False(vm.HasPlaylistChoice);
        Assert.Null(vm.VideoInfo);
        Assert.Equal(ListId, vm.Playlist!.ListId);
        Assert.Equal(2, vm.Playlist.Items.Count);
        Assert.Equal("Loaded: Pharmacology · 2 videos", vm.StatusMessage);
    }

    [Fact]
    public async Task ChooseThisVideo_HidesTheChoiceWithoutFetchingAgain()
    {
        SetupVideo(WatchUrl);
        var vm = CreateSut();
        vm.Url = WatchInList;
        await vm.FetchInfoCommand.ExecuteAsync(null);

        await vm.ChooseThisVideoCommand.ExecuteAsync(null);

        Assert.False(vm.HasPlaylistChoice);
        Assert.NotNull(vm.VideoInfo);
        Assert.Null(vm.Playlist);
        _youtube.Verify(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchInfo_MixLink_FetchesTheVideoWithTheMixHint()
    {
        SetupVideo("https://www.youtube.com/watch?v=TsYhxMnGYCw");
        var vm = CreateSut();
        vm.Url = "https://www.youtube.com/watch?v=TsYhxMnGYCw&list=RDTsYhxMnGYCw&start_radio=1";

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Equal(LinkAction.MixHint, vm.LinkHint);
        Assert.False(vm.HasPlaylistChoice);
        Assert.NotNull(vm.VideoInfo);
        _youtube.Verify(y => y.GetVideoInfoAsync("https://www.youtube.com/watch?v=TsYhxMnGYCw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchInfo_WatchLaterLink_FetchesTheVideoWithThePrivateHint()
    {
        SetupVideo("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        var vm = CreateSut();
        vm.Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=WL";

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Equal(LinkAction.PrivateListHint, vm.LinkHint);
        Assert.False(vm.HasPlaylistChoice);
        _youtube.Verify(y => y.GetVideoInfoAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchInfo_MixPlaylistUrl_IsRejectedWithoutNetwork()
    {
        var vm = CreateSut();
        vm.Url = MixPlaylistUrl;

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Equal(LinkAction.MixPlaylistMessage, vm.StatusMessage);
        Assert.Null(vm.Playlist);
        _youtube.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchInfo_UnavailablePlaylist_SaysPrivateOrMissing()
    {
        _youtube.Setup(y => y.GetPlaylistAsync(ListId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlaylistUnavailableException("x"));
        var vm = CreateSut();
        vm.Url = PlaylistUrl;

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Null(vm.Playlist);
        Assert.False(vm.IsBusy);
        Assert.Equal("This playlist is private or doesn't exist.", vm.StatusMessage);
    }

    [Fact]
    public async Task LoadPlaylist_CountAboveItemsFound_ShowsHiddenNote()
    {
        SetupPlaylist(99, Items(96));
        var vm = CreateSut();
        vm.Url = PlaylistUrl;

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Equal("3 unavailable videos are hidden by YouTube", vm.Playlist!.HiddenNote);
    }

    [Fact]
    public async Task LoadPlaylist_AboveTwoHundredItems_Warns()
    {
        SetupPlaylist(null, Items(200), [Item(201)]);
        var large = CreateSut();
        large.Url = PlaylistUrl;
        await large.FetchInfoCommand.ExecuteAsync(null);

        SetupPlaylist(null, Items(200));
        var limit = CreateSut();
        limit.Url = PlaylistUrl;
        await limit.FetchInfoCommand.ExecuteAsync(null);

        Assert.Equal("201 videos: a list this long can take hours to download.", large.Playlist!.LargeListWarning);
        Assert.Null(limit.Playlist!.LargeListWarning);
    }

    [Fact]
    public void Url_WatchLinkWithList_ShowsTheChoiceWithoutNetwork()
    {
        var vm = CreateSut();

        vm.Url = WatchInList;

        Assert.True(vm.HasPlaylistChoice);
        Assert.Equal(ListId, vm.PlaylistChoice!.ListId);

        vm.Url = "https://youtu.be/dQw4w9WgXcQ";

        Assert.False(vm.HasPlaylistChoice);
        _youtube.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Url_ChangedToAnotherLink_DropsThePlaylistCard()
    {
        var vm = await LoadedPlaylistAsync();
        var card = vm.Playlist;

        vm.Url = PlaylistUrl + "&si=x";

        Assert.Same(card, vm.Playlist);

        vm.Url = "https://youtu.be/dQw4w9WgXcQ";

        Assert.Null(vm.Playlist);
        Assert.False(vm.IsPlaylistMode);
        Assert.Equal("Press Fetch to load this link.", vm.StatusMessage);
    }

    [Fact]
    public void CheckClipboard_PlaylistLink_OffersThePlaylistBanner()
    {
        _clipboard.Setup(c => c.GetText()).Returns(PlaylistUrl);
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal(PlaylistUrl, vm.DetectedClipboardUrl);
        Assert.Equal("📋 Found a YouTube playlist on your clipboard.", vm.DetectedClipboardMessage);

        _clipboard.Setup(c => c.GetText()).Returns("https://youtu.be/dQw4w9WgXcQ");
        vm.CheckClipboardCommand.Execute(null);

        Assert.Equal("📋 Found a YouTube link on your clipboard.", vm.DetectedClipboardMessage);
    }

    [Fact]
    public void CheckClipboard_MixPlaylistUrl_IsNotOffered()
    {
        _clipboard.Setup(c => c.GetText()).Returns(MixPlaylistUrl);
        var vm = CreateSut();

        vm.CheckClipboardCommand.Execute(null);

        Assert.Null(vm.DetectedClipboardUrl);
    }

    [Fact]
    public async Task FetchInfo_VideoAfterAPlaylist_LeavesPlaylistMode()
    {
        var vm = await LoadedPlaylistAsync();
        SetupVideo(WatchUrl);
        vm.Url = WatchInList; // same list: the card stays until the video is fetched
        Assert.NotNull(vm.Playlist);

        await vm.FetchInfoCommand.ExecuteAsync(null);

        Assert.Null(vm.Playlist);
        Assert.False(vm.IsPlaylistMode);
        Assert.True(vm.ShowSingleDownload);
        Assert.NotNull(vm.VideoInfo);
        Assert.True(vm.HasPlaylistChoice);
    }
}
