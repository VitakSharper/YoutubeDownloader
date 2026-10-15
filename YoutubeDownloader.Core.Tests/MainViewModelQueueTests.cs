using System.Diagnostics;
using Moq;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.Tests;

/// <summary>
/// The playlist queue run from the Download tab (plan 026b). The pipeline is a mock (it has its own tests); the
/// folders are real, under a scratch root deleted afterwards. A gated download waits on its token, never on a
/// timer. The fetch-ahead and the current download can continue on different threads here (xUnit 2's async
/// context), so tests assert final row states, history and the final status after the run, and every awaited
/// run has a 5 s guard so a bug fails instead of hanging.
/// </summary>
public class MainViewModelQueueTests : IDisposable
{
    private const string ListId = "PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e";
    private const string PlaylistUrl = "https://www.youtube.com/playlist?list=" + ListId;
    private const string Mp3Label = "MP3 · VBR V0 (~245 kbps)";
    private const string FfmpegPath = @"C:\ffmpeg\ffmpeg.exe";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private readonly Mock<IYouTubeService> _youtube = new();
    private readonly Mock<IFFmpegLocator> _ffmpeg = new();
    private readonly Mock<IDownloadPipeline> _pipeline = new();
    private readonly Mock<ISaveFileService> _saveFile = new();
    private readonly Mock<IHistoryStore> _history = new();
    private readonly Mock<ILinkOpener> _linkOpener = new();
    private readonly Mock<IFileRevealer> _fileRevealer = new();
    private readonly Mock<ISettingsStore> _settings = new();
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<IConfirmationService> _confirm = new();
    private readonly Mock<IFoobar2000Launcher> _foobar = new();
    private readonly Mock<IFolderPickerService> _folderPicker = new();
    private readonly Mock<IRetryDelay> _retryDelay = new();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdl-queue-tests", Guid.NewGuid().ToString("N"));
    private readonly List<HistoryEntry> _storedHistory = new();
    private AppSettings _storedSettings;

    public MainViewModelQueueTests()
    {
        Directory.CreateDirectory(_root);
        _storedSettings = new AppSettings { LastSaveFolder = _root };
        _history.Setup(h => h.Load()).Returns(() => _storedHistory.ToList());
        _settings.Setup(s => s.Load()).Returns(() => _storedSettings);
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _retryDelay.Setup(d => d.WaitAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(FfmpegPath);
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) => Info(url));
        SetupPipeline((request, _, _) => Task.FromResult(Saved(request)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup of the scratch root
        }
    }

    private MainViewModel CreateSut() =>
        new(_youtube.Object, _ffmpeg.Object, _pipeline.Object,
            _saveFile.Object, _history.Object, _linkOpener.Object, _fileRevealer.Object, _settings.Object,
            _clipboard.Object, _confirm.Object, _foobar.Object, _folderPicker.Object, _retryDelay.Object);

    private static QueueItem Item(int i) =>
        new($"vid{i:D8}", PlaylistMapping.WatchUrl($"vid{i:D8}"), $"Title {i}", "Channel", TimeSpan.FromMinutes(3), i, null);

    /// <summary>The item index from a URL built by <see cref="Item"/>.</summary>
    private static int IndexOf(string url) => int.Parse(url[^8..]);

    private static VideoInfo Info(string url) => new(
        SourceUrl: url,
        Title: $"Title {IndexOf(url)}",
        Author: "Channel",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions:
        [
            new VideoStreamOption("1080p", 1080, "mp4", Source: null!),
            new VideoStreamOption("720p", 720, "mp4", Source: null!),
            new VideoStreamOption("360p", 360, "mp4", Source: null!)
        ],
        BestOpusAudio: new AudioStreamOption("opus", 160, "webm", Source: null!));

    private string SubFolder => Path.Combine(_root, "Pharmacology");

    private string Target(int i, string folder, string extension = "mp3") => Path.Combine(folder, $"{i:D3}. Channel - Title {i}.{extension}");

    private static DownloadOutcome Saved(DownloadRequest request) => new DownloadOutcome.Saved(request.TargetPath, Mp3Label);

    private void SetupPipeline(Func<DownloadRequest, IProgress<PipelineProgress>, CancellationToken, Task<DownloadOutcome>> run)
        => _pipeline.Setup(p => p.RunAsync(It.IsAny<DownloadRequest>(), It.IsAny<IProgress<PipelineProgress>>(), It.IsAny<CancellationToken>()))
                    .Returns(run);

    /// <summary>Item <paramref name="gated"/> reports "downloading" and then runs until its token is cancelled; the others save at once.</summary>
    private void SetupGatedPipeline(int gated, List<CancellationToken>? tokens = null)
        => SetupPipeline((request, progress, ct) =>
        {
            if (request.TargetPath != Target(gated, SubFolder))
                return Task.FromResult(Saved(request));
            if (tokens is not null)
            {
                lock (tokens)
                    tokens.Add(ct);
            }
            progress.Report(new PipelineProgress(PipelineStage.DownloadingAudio, 0.3));
            return RunUntilCancelledAsync(ct);
        });

    /// <summary>A download that runs until its token is cancelled, then answers Cancelled as DownloadPipeline does.</summary>
    private static async Task<DownloadOutcome> RunUntilCancelledAsync(CancellationToken ct)
    {
        try
        {
            await new TaskCompletionSource().Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return new DownloadOutcome.Cancelled();
        }
        throw new UnreachableException("The gate never opens.");
    }

    private void SetupPlaylist(int count)
    {
        _youtube.Setup(y => y.GetPlaylistAsync(ListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaylistInfo(ListId, "Pharmacology", "Armando Hasudungan", count, null));
        _youtube.Setup(y => y.GetPlaylistItemBatchesAsync(ListId, It.IsAny<CancellationToken>()))
            .Returns(() => Batches(Enumerable.Range(1, count).Select(Item).ToList()));
    }

    /// <summary>Pages of 200, like YouTube.</summary>
    private static async IAsyncEnumerable<IReadOnlyList<QueueItem>> Batches(IReadOnlyList<QueueItem> items)
    {
        foreach (var batch in items.Chunk(200))
            yield return batch;
        await Task.CompletedTask;   // keeps the compiler quiet about an async iterator without await
    }

    /// <summary>A view model showing the "Pharmacology" card with <paramref name="count"/> ticked items.</summary>
    private async Task<MainViewModel> LoadedPlaylistAsync(int count = 3)
    {
        SetupPlaylist(count);
        var vm = CreateSut();
        vm.Url = PlaylistUrl;
        await vm.FetchInfoCommand.ExecuteAsync(null);
        Assert.Equal(count, vm.Playlist!.Items.Count);
        return vm;
    }

    private static QueueItemViewModel Row(MainViewModel vm, int i) => vm.Playlist!.Items[i - 1];

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, Guard), "Timed out waiting for the run.");

    private DownloadRequest[] Requests() => _pipeline.Invocations.Select(i => (DownloadRequest)i.Arguments[0]).ToArray();

    private int FetchCount(int i) => _youtube.Invocations.Count(inv =>
        inv.Method.Name == nameof(IYouTubeService.GetVideoInfoAsync) && Equals(inv.Arguments[0], Item(i).Url));

    [Fact]
    public async Task DownloadPlaylist_NoFolderYet_AsksStartingAtLastSaveFolder()
    {
        _storedSettings = new AppSettings { LastSaveFolder = "" };
        _folderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(_root);
        var vm = await LoadedPlaylistAsync();
        Assert.Equal("Chosen when you start", vm.PlaylistTargetFolder);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        _folderPicker.Verify(p => p.PickFolderAsync("Choose where to save the playlist", ""), Times.Once);
        _settings.Verify(s => s.Save(It.Is<AppSettings>(a => a.LastSaveFolder == _root)), Times.AtLeastOnce);
        Assert.Equal(_root, vm.PlaylistFolder);
        Assert.Equal(SubFolder, vm.PlaylistTargetFolder);
        Assert.Equal("3 of 3 saved", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadPlaylist_FolderPickerCancelled_StartsNothing()
    {
        _storedSettings = new AppSettings { LastSaveFolder = "" };
        _folderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync((string?)null);
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal("Download cancelled.", vm.StatusMessage);
        _ffmpeg.Verify(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_pipeline.Invocations);
        Assert.False(vm.IsBusy);
        Assert.All(vm.Playlist!.Items, row => Assert.IsType<ItemState.Pending>(row.State));
    }

    [Fact]
    public async Task DownloadPlaylist_SavesTickedItemsAsNumberedArtistTitleInTheSubFolder()
    {
        var vm = await LoadedPlaylistAsync();
        Assert.Equal("Download 3", vm.DownloadPlaylistLabel);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal([Target(1, SubFolder), Target(2, SubFolder), Target(3, SubFolder)], Requests().Select(r => r.TargetPath));
        Assert.True(Directory.Exists(SubFolder));
        Assert.All(Requests(), r => Assert.Equal(new OutputFormat.Mp3(Mp3Quality.BestVbr), r.Format));
        Assert.Equal(Enumerable.Range(1, 3).Select(i => (ItemState)new ItemState.Saved(Target(i, SubFolder), Mp3Label)),
            vm.Playlist!.Items.Select(row => row.State));
        Assert.Equal("3 of 3 saved", vm.StatusMessage);
        Assert.Equal(1.0, vm.Progress, 3);
        Assert.False(vm.IsQueueRunning);
        Assert.False(vm.IsBusy);
        Assert.False(vm.ShowRetryFailed);
        Assert.Equal("Download 0", vm.DownloadPlaylistLabel);
        Assert.False(vm.DownloadPlaylistCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadPlaylist_SubFolderOff_SavesIntoTheChosenFolder()
    {
        var vm = await LoadedPlaylistAsync();
        vm.UseSubFolder = false;
        Assert.Equal(_root, vm.PlaylistTargetFolder);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal([Target(1, _root), Target(2, _root), Target(3, _root)], Requests().Select(r => r.TargetPath));
        Assert.False(Directory.Exists(SubFolder));
    }

    [Fact]
    public async Task DownloadPlaylist_EnsuresFfmpegOnceForTheWholeQueue()
    {
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        _ffmpeg.Verify(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(3, Requests().Length);
        Assert.All(Requests(), r => Assert.Equal(FfmpegPath, r.FfmpegPath));
    }

    [Fact]
    public async Task DownloadPlaylist_RecordsOneHistoryEntryPerSavedItemWithTheWatchUrl()
    {
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(3, vm.History.Count);
        foreach (var i in new[] { 1, 2, 3 })
        {
            var entry = Assert.Single(vm.History, e => e.Title == $"Title {i}");
            Assert.Equal(Item(i).Url, entry.Url);
            Assert.DoesNotContain("list=", entry.Url);
            Assert.Equal(Mp3Label, entry.Format);
            Assert.Equal(Target(i, SubFolder), entry.FilePath);
        }
        _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), Times.Once);   // plan 027b: coalesced, flushed when the run ends
    }

    [Fact]
    public async Task History_KeepsUpToOneThousandEntries()
    {
        _storedHistory.AddRange(Enumerable.Range(1, 1000).Select(i => new HistoryEntry
        {
            Url = $"https://youtu.be/old{i:D7}", Title = $"Old {i}", DownloadedAt = DateTimeOffset.Now.AddDays(-1).AddSeconds(-i)
        }));
        var vm = await LoadedPlaylistAsync(count: 1);
        Assert.Equal(1000, vm.History.Count);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(1000, vm.History.Count);
        Assert.Equal("Title 1", vm.History[0].Title);
        Assert.Equal("Old 999", vm.History[^1].Title);   // the oldest one made room
    }

    [Fact]
    public async Task QueueRun_FillsTheRunFieldsOnEveryEntry()
    {
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(3, vm.History.Count);
        var runId = vm.History[0].RunId;
        Assert.False(string.IsNullOrEmpty(runId));
        foreach (var i in new[] { 1, 2, 3 })
        {
            var entry = Assert.Single(vm.History, e => e.Title == $"Title {i}");
            Assert.Equal(runId, entry.RunId);
            Assert.Equal(Item(i).VideoId, entry.VideoId);
            Assert.Equal(ListId, entry.PlaylistId);
            Assert.Equal("Pharmacology", entry.PlaylistTitle);
            Assert.Equal(i, entry.TrackIndex);
        }
        var run = Assert.IsType<HistoryRunRow>(Assert.Single(Assert.Single(vm.HistoryGroups).Rows));
        Assert.Equal("Pharmacology — 3 saved", run.Header);
    }

    [Fact]
    public void RecordQueueHistory_SavesOncePerTenEntriesAndWhenHistoryIsOpened()
    {
        var vm = CreateSut();
        var entries = Enumerable.Range(1, MainViewModel.HistoryFlushEvery + 1)
            .Select(i => new HistoryEntry { Url = Item(i).Url, Title = $"Title {i}", DownloadedAt = DateTimeOffset.Now, RunId = "run" })
            .ToList();

        foreach (var entry in entries.Take(MainViewModel.HistoryFlushEvery - 1))
            vm.RecordQueueHistory(entry);

        VerifySaved(Times.Never());
        Assert.Empty(vm.HistoryGroups);
        Assert.Equal(MainViewModel.HistoryFlushEvery - 1, vm.History.Count);

        vm.RecordQueueHistory(entries[MainViewModel.HistoryFlushEvery - 1]);

        VerifySaved(Times.Once());
        Assert.Equal(MainViewModel.HistoryFlushEvery, Assert.Single(vm.HistoryGroups).Entries.Count);

        vm.RecordQueueHistory(entries[MainViewModel.HistoryFlushEvery]);
        VerifySaved(Times.Once());
        vm.SelectedTabIndex = 1;

        VerifySaved(Times.Exactly(2));
        Assert.Equal(MainViewModel.HistoryFlushEvery + 1, Assert.Single(vm.HistoryGroups).Entries.Count);
    }

    private void VerifySaved(Times times) => _history.Verify(h => h.Save(It.IsAny<IEnumerable<HistoryEntry>>()), times);

    [Fact]
    public async Task DownloadPlaylist_ItemAlreadySavedHere_IsSkipped()
    {
        Directory.CreateDirectory(SubFolder);
        File.WriteAllText(Target(1, SubFolder), "saved earlier");
        _storedHistory.Add(new HistoryEntry
        {
            Url = Item(1).Url, Title = "Title 1", Format = Mp3Label, FilePath = Target(1, SubFolder), DownloadedAt = DateTimeOffset.Now
        });
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(new ItemState.Skipped("Already saved"), Row(vm, 1).State);
        Assert.Equal([Target(2, SubFolder), Target(3, SubFolder)], Requests().Select(r => r.TargetPath));
        Assert.Equal(0, FetchCount(1));
        Assert.Contains("1 skipped", vm.StatusMessage);
        Assert.Equal("2 of 3 saved · 1 skipped", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadPlaylist_UntickedItems_AreLeftOut()
    {
        var vm = await LoadedPlaylistAsync();
        Row(vm, 2).IsSelected = false;
        Assert.Equal("Download 2", vm.DownloadPlaylistLabel);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal([Target(1, SubFolder), Target(3, SubFolder)], Requests().Select(r => r.TargetPath));
        Assert.Equal(0, FetchCount(2));
        Assert.IsType<ItemState.Pending>(Row(vm, 2).State);
        Assert.Equal("2 of 2 saved", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadPlaylist_VideoMode_UsesTheHeightCap()
    {
        var vm = await LoadedPlaylistAsync();
        vm.SelectedMode = DownloadMode.Video;
        vm.SelectedHeightCap = vm.HeightCaps.Single(cap => cap.MaxHeight == 720);

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(3, Requests().Length);
        Assert.All(Requests(), r => Assert.Equal(720, Assert.IsType<OutputFormat.Mp4>(r.Format).Video.Height));
        Assert.Equal([Target(1, SubFolder, "mp4"), Target(2, SubFolder, "mp4"), Target(3, SubFolder, "mp4")],
            Requests().Select(r => r.TargetPath));
    }

    [Fact]
    public async Task DownloadPlaylist_TagAsAlbum_PassesAlbumAndTrackTags()
    {
        // A YouTube Music album (OLAK5uy_…): "Tag as an album" starts ticked.
        const string albumId = "OLAK5uy_lLeonUugocG5J0EUAEDmbskX4emejKwcM";
        _youtube.Setup(y => y.GetPlaylistAsync(albumId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaylistInfo(albumId, "Pharmacology", null, 3, null));
        _youtube.Setup(y => y.GetPlaylistItemBatchesAsync(albumId, It.IsAny<CancellationToken>()))
            .Returns(() => Batches(Enumerable.Range(1, 3).Select(Item).ToList()));
        var album = CreateSut();
        album.Url = "https://music.youtube.com/playlist?list=" + albumId;
        await album.FetchInfoCommand.ExecuteAsync(null);
        Assert.True(album.Playlist!.TagAsAlbum);
        Assert.True(album.ShowAlbumTagging);

        await album.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(3, Requests().Length);
        Assert.All(Requests(), r =>
        {
            Assert.Equal("Pharmacology", r.Tags.Album);
            Assert.Equal(IndexOf(r.Tags.SourceUrl), r.Tags.Track);   // the item's playlist index
            Assert.Equal(3, r.Tags.TrackCount);
        });

        // An ordinary list: the box starts unticked, so no album tags.
        _pipeline.Invocations.Clear();
        var ordinary = await LoadedPlaylistAsync();
        Assert.False(ordinary.Playlist!.TagAsAlbum);

        await ordinary.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(3, Requests().Length);
        Assert.All(Requests(), r =>
        {
            Assert.Null(r.Tags.Album);
            Assert.Null(r.Tags.Track);
            Assert.Null(r.Tags.TrackCount);
        });
    }

    [Fact]
    public async Task CancelAll_Confirmed_CancelsTheRunAndMarksTheRestCancelled()
    {
        SetupGatedPipeline(1);
        var vm = await LoadedPlaylistAsync();

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);
        Assert.True(vm.CancelAllCommand.CanExecute(null));
        await vm.CancelAllCommand.ExecuteAsync(null);
        await run.WaitAsync(Guard);

        _confirm.Verify(c => c.ConfirmAsync("Cancel the remaining 3 downloads? Files already saved are kept.", "Cancel all?"), Times.Once);
        Assert.All(vm.Playlist!.Items, row => Assert.IsType<ItemState.Cancelled>(row.State));
        Assert.StartsWith("Cancelled. ", vm.StatusMessage);
        Assert.Equal("Cancelled. 0 of 3 saved · 3 cancelled", vm.StatusMessage);
        Assert.False(vm.IsQueueRunning);
        Assert.True(vm.HasRetryableItems);
        Assert.True(vm.ShowRetryFailed);
        Assert.All(vm.Playlist.Items, row => Assert.False(row.CanRemove));
    }

    [Fact]
    public async Task CancelAll_Declined_KeepsRunning()
    {
        _confirm.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        var tokens = new List<CancellationToken>();
        SetupGatedPipeline(1, tokens);
        var vm = await LoadedPlaylistAsync();

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);
        await vm.CancelAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsQueueRunning);
        Assert.False(run.IsCompleted);
        Assert.False(Assert.Single(tokens).IsCancellationRequested);

        vm.RemoveQueueItemCommand.Execute(Row(vm, 1));   // finish the test: drop the gated item only
        await run.WaitAsync(Guard);
        Assert.Equal("2 of 3 saved · 1 cancelled", vm.StatusMessage);
    }

    [Fact]
    public async Task RemoveQueueItem_DuringTheRun_CancelsOnlyThatItem()
    {
        SetupGatedPipeline(1);
        var vm = await LoadedPlaylistAsync();

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);
        Assert.True(Row(vm, 1).CanRemove);
        vm.RemoveQueueItemCommand.Execute(Row(vm, 1));
        await run.WaitAsync(Guard);

        _confirm.Verify(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsType<ItemState.Cancelled>(Row(vm, 1).State);
        Assert.IsType<ItemState.Saved>(Row(vm, 2).State);
        Assert.IsType<ItemState.Saved>(Row(vm, 3).State);
        Assert.Equal("2 of 3 saved · 1 cancelled", vm.StatusMessage);
        Assert.True(vm.ShowRetryFailed);
    }

    [Fact]
    public async Task RunFinished_ShowsTheSummary_AndRetryFailedRunsOnlyTheFailedItems()
    {
        _youtube.Setup(y => y.GetVideoInfoAsync(Item(2).Url, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new VideoUnavailableException("Video is unavailable"));
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal("2 of 3 saved · 1 failed", vm.StatusMessage);
        Assert.Equal(new ItemState.Failed("Unavailable (private or removed)", FailureKind.Unavailable), Row(vm, 2).State);
        Assert.True(Row(vm, 2).IsFailed);
        Assert.Equal("Unavailable (private or removed)", Row(vm, 2).StatusToolTip);
        Assert.True(vm.ShowRetryFailed);
        Assert.True(vm.RetryFailedCommand.CanExecute(null));
        Assert.Equal("Download 1", vm.DownloadPlaylistLabel);

        _youtube.Setup(y => y.GetVideoInfoAsync(Item(2).Url, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) => Info(url));
        await vm.RetryFailedCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.Equal(2, FetchCount(2));
        Assert.Equal(1, FetchCount(1));
        Assert.Equal(1, FetchCount(3));
        Assert.Equal(Target(2, SubFolder), Requests().Last().TargetPath);
        Assert.Equal(3, Requests().Length);   // items 1 and 3 in the first run, item 2 in the retry
        Assert.IsType<ItemState.Saved>(Row(vm, 2).State);
        Assert.Equal("1 of 1 saved", vm.StatusMessage);
        Assert.False(vm.ShowRetryFailed);
    }

    [Fact]
    public async Task RateLimitedStop_SaysResumeLater()
    {
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestLimitExceededException("Too many requests"));
        var vm = await LoadedPlaylistAsync();

        await vm.DownloadPlaylistCommand.ExecuteAsync(null).WaitAsync(Guard);

        Assert.StartsWith("YouTube is rate-limiting — resume later. ", vm.StatusMessage);
        Assert.Equal("YouTube is rate-limiting — resume later. 0 of 3 saved · 3 not started", vm.StatusMessage);
        Assert.Empty(_pipeline.Invocations);
        Assert.False(vm.ShowRetryFailed);
        Assert.Equal("Download 3", vm.DownloadPlaylistLabel);   // "Download N" resumes later
        Assert.True(vm.DownloadPlaylistCommand.CanExecute(null));
    }

    [Fact]
    public async Task QueueRunning_BlocksFetchUseAndSingleDownload()
    {
        SetupGatedPipeline(1);
        var vm = await LoadedPlaylistAsync();

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);

        Assert.True(vm.IsBusy);
        Assert.True(vm.IsQueueRunning);
        Assert.False(vm.FetchInfoCommand.CanExecute(null));
        Assert.False(vm.UseEntryCommand.CanExecute(new HistoryEntry { Url = Item(1).Url }));
        Assert.False(vm.DownloadPlaylistCommand.CanExecute(null));
        Assert.False(vm.DownloadCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));
        Assert.False(vm.ChangePlaylistFolderCommand.CanExecute(null));
        Assert.False(vm.ShowPlaylistDownload);
        Assert.True(vm.CancelAllCommand.CanExecute(null));

        await vm.CancelAllCommand.ExecuteAsync(null);
        await run.WaitAsync(Guard);
        Assert.False(vm.CancelAllCommand.CanExecute(null));
        Assert.True(vm.FetchInfoCommand.CanExecute(null));
    }

    [Fact]
    public async Task StopQueueAsync_CancelsARunningQueue()
    {
        var tokens = new List<CancellationToken>();
        SetupGatedPipeline(1, tokens);
        var vm = await LoadedPlaylistAsync();

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);
        await vm.StopQueueAsync(TimeSpan.FromSeconds(5)).WaitAsync(Guard);

        Assert.True(Assert.Single(tokens).IsCancellationRequested);
        Assert.False(vm.IsQueueRunning);
        Assert.True(run.IsCompleted);
        _confirm.Verify(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.StartsWith("Cancelled. ", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadPlaylist_LongList_HidesTheLongListWarningOnceTheRunStarts()
    {
        SetupGatedPipeline(1);
        var vm = await LoadedPlaylistAsync(count: 201);
        Assert.Equal("201 videos: a list this long can take hours to download.", vm.Playlist!.LargeListWarning);

        var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
        WaitUntil(() => Row(vm, 1).State is ItemState.Downloading);

        Assert.Null(vm.Playlist.LargeListWarning);

        await vm.StopQueueAsync(TimeSpan.FromSeconds(5)).WaitAsync(Guard);
        await run.WaitAsync(Guard);
        Assert.Null(vm.Playlist.LargeListWarning);   // it has done its job: it stays hidden for this card
    }
}
