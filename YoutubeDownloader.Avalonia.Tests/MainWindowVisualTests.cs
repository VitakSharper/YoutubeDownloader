using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;
using YoutubeExplode.Exceptions;
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
    private const string PlaylistUrl = "https://www.youtube.com/playlist?list=PL601B2E69B03FAB9D";
    private const string WatchInListUrl = "https://www.youtube.com/watch?v=uPZSSdkGQhM&list=PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e&index=1";

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
    private readonly Mock<IClipboardService> _clipboard = new();
    private readonly Mock<IFolderPickerService> _folderPicker = new();

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
    /// Plan 027b: a playlist run's entries sit under one collapsible sub-header in their date group, between
    /// single downloads; it starts collapsed, expands from the row or the control, and a search opens it.
    /// </summary>
    [AvaloniaFact]
    public void History_RunEntries_ShowACollapsibleSubHeader()
    {
        const string header = "'80s Sing-Alongs — 3 saved";
        string[] runTitles = ["Take On Me", "Total Eclipse of the Heart", "Don't Stop Believin'"];
        var today = DateTime.Today;
        _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
        {
            new() { Url = VideoUrl, Title = "Me at the zoo", Format = "MP3 · VBR V0 (~245 kbps)", FilePath = @"C:\Music\Me at the zoo.mp3", DownloadedAt = today.AddHours(21) },
            RunEntry(3, runTitles[0], today.AddHours(20).AddMinutes(14)),
            RunEntry(2, runTitles[1], today.AddHours(20).AddMinutes(9)),
            RunEntry(1, runTitles[2], today.AddHours(20).AddMinutes(4)),
            new() { Url = "https://www.youtube.com/watch?v=9bZkp7q19f0", Title = "Gangnam Style", Format = "MP4 · 1080p", DownloadedAt = today.AddHours(19) }
        });
        var (window, vm, _) = Create();

        vm.SelectedTabIndex = 1;
        Settle();

        var texts = VisibleTexts(window);
        Assert.Contains("Today", texts);
        Assert.Contains(header, texts);
        Assert.Contains("Me at the zoo", texts);
        Assert.Contains("Gangnam Style", texts);
        Assert.All(runTitles, title => Assert.DoesNotContain(title, texts));
        var expander = window.GetVisualDescendants().OfType<Expander>().Single();
        Assert.False(expander.IsExpanded);
        Snapshot(window, "027b-history-collapsed");

        // Expanding the row (as a regrouping would restore it) opens the control; the cards are the single entries' cards.
        var run = Assert.IsType<HistoryRunRow>(vm.HistoryGroups.Single().Rows[1]);
        run.IsExpanded = true;
        Settle();

        Assert.True(expander.IsExpanded);
        texts = VisibleTexts(window);
        Assert.All(runTitles, title => Assert.Contains(title, texts));
        Assert.Contains(header, texts);
        var useButtons = expander.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "Use")).ToList();
        Assert.Equal(3, useButtons.Count);
        Assert.All(useButtons, b => Assert.Same(vm.UseEntryCommand, b.Command));
        Snapshot(window, "027b-history-expanded");

        // Collapsing the control writes back to the row.
        expander.IsExpanded = false;
        Settle();
        Assert.False(run.IsExpanded);
        Assert.All(runTitles, title => Assert.DoesNotContain(title, VisibleTexts(window)));

        // A search opens every matching run without remembering it: clearing the search folds it again.
        vm.SearchText = "watch";
        Settle();
        texts = VisibleTexts(window);
        Assert.Contains(header, texts);
        Assert.All(runTitles, title => Assert.Contains(title, texts));
        Assert.Contains("Gangnam Style", texts);
        Assert.DoesNotContain("Me at the zoo", texts);
        Snapshot(window, "027b-history-search");

        vm.SearchText = "";
        Settle();
        texts = VisibleTexts(window);
        Assert.Contains(header, texts);
        Assert.All(runTitles, title => Assert.DoesNotContain(title, texts));
    }

    private static HistoryEntry RunEntry(int track, string title, DateTime downloadedAt) => new()
    {
        Url = PlaylistMapping.WatchUrl($"vid{track:D8}"),
        Title = title,
        Format = "MP3 · VBR V0 (~245 kbps)",
        FilePath = $@"C:\Music\'80s Sing-Alongs\{track:D3}. {title}.mp3",
        DownloadedAt = downloadedAt,
        VideoId = $"vid{track:D8}",
        PlaylistId = "PL601B2E69B03FAB9D",
        PlaylistTitle = "'80s Sing-Alongs",
        RunId = "0123456789abcdef0123456789abcdef",
        TrackIndex = track
    };

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

    [AvaloniaFact]
    public async Task PlaylistUrl_ShowsCardChecklistAndSelectAll()
    {
        var (window, vm, _) = Create();

        await FetchPlaylistAsync(vm, PharmacologyInfo(count: 5), PharmacologyItems());

        var texts = VisibleTexts(window);
        Assert.Contains("Pharmacology", texts);
        Assert.Contains("Armando Hasudungan · 5 videos", texts);
        Assert.Contains("Select all (5 of 5)", texts);
        Assert.Contains("Etanercept", texts);
        Assert.Contains("001", texts);
        Assert.Contains("12:16", texts);
        Assert.Single(texts, t => t == "MP3 QUALITY");
        Assert.DoesNotContain("VIDEO QUALITY", texts);
        var checklist = Checklist(window);
        Assert.True(checklist.Bounds.Height >= 112, $"The checklist is {checklist.Bounds.Height} px tall; 4 rows need 112 px.");
        Snapshot(window, "026a-playlist-card");
    }

    [AvaloniaFact]
    public async Task WatchLinkWithList_ShowsTheChoiceBanner()
    {
        var (window, vm, _) = Create();

        vm.Url = WatchInListUrl;
        Settle();

        var texts = VisibleTexts(window);
        Assert.Contains("This link is part of a playlist.", texts);
        Assert.Contains("This video", texts);
        Assert.Contains("Whole playlist", texts);

        // Fetch keeps the banner and loads the video under it: the busiest layout of this state.
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(SampleInfo());
        await vm.FetchInfoCommand.ExecuteAsync(null);
        Settle();
        Assert.Contains("Whole playlist", VisibleTexts(window));
        Snapshot(window, "026a-choice-banner");
    }

    [AvaloniaFact]
    public async Task LargePlaylist_IsVirtualizedAndWarns()
    {
        var (window, vm, _) = Create();

        await FetchPlaylistAsync(vm, PharmacologyInfo(count: 302), LongItems(300));

        var realized = window.GetVisualDescendants().OfType<ListBoxItem>().Count();
        Assert.InRange(realized, 1, 299);
        var texts = VisibleTexts(window);
        Assert.Contains("300 videos: a list this long can take hours to download.", texts);
        Assert.Contains("2 unavailable videos are hidden by YouTube", texts);
        Assert.Contains("Select all (300 of 300)", texts);
        Snapshot(window, "026a-large-list");
    }

    [AvaloniaFact]
    public void PlaylistOnClipboard_ShowsThePlaylistBanner()
    {
        var (window, vm, _) = Create();
        _clipboard.Setup(c => c.GetText()).Returns(PlaylistUrl);

        vm.CheckClipboardCommand.Execute(null);
        Settle();

        Assert.Contains("📋 Found a YouTube playlist on your clipboard.", VisibleTexts(window));
        Snapshot(window, "026a-clipboard-playlist");
    }

    [AvaloniaFact]
    public async Task QueueRunning_ShowsRowStatusProgressAndCancelAll()
    {
        var folder = NewScratchFolder();
        try
        {
            // Item 1's stream reports half done and then runs until cancelled; item 2 is fetched ahead meanwhile.
            SetupQueue(folder, (_, progress, ct) =>
            {
                progress?.Report(0.5);
                return Task.Delay(Timeout.Infinite, ct);
            });
            var (window, vm, _) = Create();
            await FetchPlaylistAsync(vm, PharmacologyInfo(count: 5), PharmacologyItems());

            var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
            Settle();

            var texts = VisibleTexts(window);
            Assert.Contains("Cancel all", texts);
            Assert.Contains("Downloading…", texts);
            Assert.Contains("Fetching…", texts);
            Assert.DoesNotContain("Download 5", texts);
            var checklist = Checklist(window);
            var downloading = RowItem(checklist, 1);
            var miniBar = downloading.GetVisualDescendants().OfType<ProgressBar>().Single();
            Assert.True(miniBar.IsEffectivelyVisible);
            Assert.Equal(60, miniBar.Bounds.Width);
            Assert.Equal(0.5 * 2 / 3, miniBar.Value, 3);   // the stream download is the first two thirds of the pipeline
            Assert.All(Enumerable.Range(1, 5), i => Assert.True(RemoveButton(RowItem(checklist, i)).IsEffectivelyVisible));
            Assert.False(RowItem(checklist, 3).GetVisualDescendants().OfType<ProgressBar>().Single().IsEffectivelyVisible);
            AssertDurationsAligned(checklist);
            Snapshot(window, "026b-queue-running");

            await vm.CancelAllCommand.ExecuteAsync(null);
            await SettleUntilAsync(() => run.IsCompleted);
            Assert.True(run.IsCompleted);
            Assert.Equal("Cancelled. 0 of 5 saved · 5 cancelled", vm.StatusMessage);
        }
        finally
        {
            DeleteScratchFolder(folder);
        }
    }

    /// <summary>Every final state in one checklist: saved, two failures (one too long for its cell), skipped and cancelled.</summary>
    [AvaloniaFact]
    public async Task QueueFinished_ShowsSummaryAndRetryFailed()
    {
        var folder = NewScratchFolder();
        try
        {
            // Item 4 was saved here before, so the run skips it.
            var items = PharmacologyItems();
            var subFolder = OutputFileNamer.SubFolder(folder, "Pharmacology");
            var savedEarlier = OutputFileNamer.Plan([new NamingEntry(4, items[3].Title, items[3].Author)], subFolder, "mp3", _ => false)[0];
            Directory.CreateDirectory(subFolder);
            File.WriteAllText(savedEarlier, "saved earlier");
            _history.Setup(h => h.Load()).Returns(new List<HistoryEntry>
            {
                new() { Url = items[3].Url, Title = items[3].Title, Format = "MP3 · VBR V0 (~245 kbps)", FilePath = savedEarlier, DownloadedAt = DateTimeOffset.Now }
            });
            // Items 2 and 3 fail at fetch; item 5 downloads until its ✕.
            SetupQueue(folder,
                (videoId, _, ct) => videoId == items[4].VideoId ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask,
                url => url == items[1].Url ? new VideoUnavailableException("Video is unavailable")
                    : url == items[2].Url ? new VideoUnplayableException("Video is age-restricted")
                    : null);
            var (window, vm, _) = Create();
            await FetchPlaylistAsync(vm, PharmacologyInfo(count: 5), items);

            var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
            Settle();
            vm.RemoveQueueItemCommand.Execute(vm.Playlist!.Items[4]);
            await SettleUntilAsync(() => run.IsCompleted);

            Assert.True(run.IsCompleted);
            var summary = "1 of 5 saved · 2 failed · 1 skipped · 1 cancelled";
            Assert.Equal(summary, vm.StatusMessage);
            var texts = VisibleTexts(window);
            Assert.Contains(summary, texts);
            Assert.Contains("✓ Saved", texts);
            Assert.Contains("Unavailable (private or removed)", texts);
            Assert.Contains("Already saved", texts);
            Assert.Contains("Cancelled", texts);
            Assert.Contains("Retry failed", texts);
            Assert.Contains("Download 4", texts);
            Assert.DoesNotContain("Cancel all", texts);

            var checklist = Checklist(window);
            var unplayable = StatusOf(RowItem(checklist, 3));
            Assert.Equal("Can't be played (age-restricted or region-blocked)", unplayable.Text);
            Assert.Contains(unplayable.TextLayout.TextLines, line => line.HasCollapsed);   // ends in an ellipsis…
            Assert.Equal(unplayable.Text, ToolTip.GetTip(unplayable));                     // …and reads in full in the tooltip
            Assert.Equal(Color.Parse("#FCA5A5"), ((ISolidColorBrush)unplayable.Foreground!).Color);
            Assert.Equal(Color.Parse("#FCA5A5"), ((ISolidColorBrush)StatusOf(RowItem(checklist, 2)).Foreground!).Color);
            Assert.Equal(Color.Parse("#B8B0D0"), ((ISolidColorBrush)StatusOf(RowItem(checklist, 1)).Foreground!).Color);
            Assert.Null(ToolTip.GetTip(StatusOf(RowItem(checklist, 1))));
            Assert.All(Enumerable.Range(1, 5), i => Assert.False(RemoveButton(RowItem(checklist, i)).IsEffectivelyVisible));
            AssertDurationsAligned(checklist);
            Snapshot(window, "026b-queue-finished");

            // The last rows, the cancelled one in full.
            checklist.ScrollIntoView(4);
            Settle();
            Assert.Equal("Cancelled", StatusOf(RowItem(checklist, 5)).Text);
            AssertDurationsAligned(checklist);
            Snapshot(window, "026b-queue-finished-scrolled");
        }
        finally
        {
            DeleteScratchFolder(folder);
        }
    }

    [AvaloniaFact]
    public async Task PlaylistOptions_VideoMode_ShowMaxVideoHeight()
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastMode = "Video" });
        var (window, vm, _) = Create();

        await FetchPlaylistAsync(vm, PharmacologyInfo(count: 5), PharmacologyItems());

        var texts = VisibleTexts(window);
        Assert.Contains("MAX VIDEO HEIGHT", texts);
        Assert.Contains("1080p", texts);
        Assert.Contains("SAVE TO", texts);
        Assert.Contains("Chosen when you start", texts);
        Assert.Contains("Sub-folder per playlist", texts);
        Assert.Contains("Download 5", texts);
        Assert.DoesNotContain("MP3 QUALITY", texts);
        Assert.DoesNotContain("VIDEO QUALITY", texts);
        var checklist = Checklist(window);
        Assert.True(checklist.Bounds.Height >= 112, $"The checklist is {checklist.Bounds.Height} px tall; 4 rows need 112 px.");
        Snapshot(window, "026b-options-video");
    }

    [AvaloniaFact]
    public async Task LargeQueueRunning_HidesTheLongListWarning()
    {
        var folder = NewScratchFolder();
        try
        {
            SetupQueue(folder, (_, progress, ct) =>
            {
                progress?.Report(0.25);
                return Task.Delay(Timeout.Infinite, ct);
            });
            var (window, vm, _) = Create();
            await FetchPlaylistAsync(vm, PharmacologyInfo(count: 302), LongItems(300));
            const string warning = "300 videos: a list this long can take hours to download.";
            Assert.Contains(warning, VisibleTexts(window));
            var before = Checklist(window).Bounds.Height;

            var run = vm.DownloadPlaylistCommand.ExecuteAsync(null);
            Settle();

            Assert.DoesNotContain(warning, VisibleTexts(window));
            var after = Checklist(window).Bounds.Height;
            Assert.True(after >= before + 30, $"The checklist grew from {before} to {after} px only.");   // the warning and its spacing
            Assert.Contains("Cancel all", VisibleTexts(window));
            AssertDurationsAligned(Checklist(window));
            Snapshot(window, "026b-large-list-running");

            await vm.StopQueueAsync(TimeSpan.FromSeconds(3));
            await SettleUntilAsync(() => run.IsCompleted);
            Assert.True(run.IsCompleted);
        }
        finally
        {
            DeleteScratchFolder(folder);
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WhileTheQueueRuns_CancelsIt()
    {
        var folder = NewScratchFolder();
        try
        {
            var tokens = new List<CancellationToken>();
            SetupQueue(folder, (_, _, ct) =>
            {
                tokens.Add(ct);
                return Task.Delay(Timeout.Infinite, ct);
            });
            var (window, vm, _) = Create();
            await FetchPlaylistAsync(vm, PharmacologyInfo(count: 5), PharmacologyItems());
            _ = vm.DownloadPlaylistCommand.ExecuteAsync(null);
            Settle();
            Assert.True(vm.IsQueueRunning);

            window.Close();

            // The close is held while the queue stops: the token is cancelled, the window still open.
            Assert.True(Assert.Single(tokens).IsCancellationRequested);
            Assert.True(window.IsVisible);
            for (var i = 0; i < 50 && window.IsVisible; i++)
            {
                await Task.Delay(20);
                Settle();
            }

            Assert.False(window.IsVisible);
            Assert.False(vm.IsQueueRunning);
        }
        finally
        {
            DeleteScratchFolder(folder);
        }
    }

    private (MainWindow Window, MainViewModel Vm, UpdateViewModel Update) Create()
    {
        var vm = new MainViewModel(_youtube.Object, _ffmpeg.Object,
            new DownloadPipeline(_youtube.Object, _converter.Object, _temp.Object, Mock.Of<IOutputFileService>(), Mock.Of<IThumbnailFetcher>()),
            _saveFile.Object, _history.Object, Mock.Of<ILinkOpener>(), Mock.Of<IFileRevealer>(), _settings.Object,
            _clipboard.Object, _confirm.Object, Mock.Of<IFoobar2000Launcher>(), _folderPicker.Object, Mock.Of<IRetryDelay>());
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

    private async Task FetchPlaylistAsync(MainViewModel vm, PlaylistInfo info, IReadOnlyList<QueueItem> items)
    {
        _youtube.Setup(y => y.GetPlaylistAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _youtube.Setup(y => y.GetPlaylistItemBatchesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Batches(items));
        vm.Url = PlaylistUrl;
        await vm.FetchInfoCommand.ExecuteAsync(null);
        Settle();
    }

    /// <summary>Pages of 200, like YouTube.</summary>
    private static async IAsyncEnumerable<IReadOnlyList<QueueItem>> Batches(IReadOnlyList<QueueItem> items)
    {
        foreach (var batch in items.Chunk(200))
            yield return batch;
        await Task.CompletedTask;   // keeps the compiler quiet about an async iterator without await
    }

    private static PlaylistInfo PharmacologyInfo(int count) =>
        new("PL601B2E69B03FAB9D", "Pharmacology", "Armando Hasudungan", count, null);

    /// <summary>The first five entries of the spike's Pharmacology list.</summary>
    private static IReadOnlyList<QueueItem> PharmacologyItems() =>
    [
        Entry(1, "Nociceptors - An Introduction to Pain", TimeSpan.FromSeconds(736)),
        Entry(2, "G Protein Signalling", TimeSpan.FromSeconds(568)),
        Entry(3, "Part I - Aracidonic Acid Metabolites", TimeSpan.FromSeconds(600)),
        Entry(4, "Part II - Aracidonic Acid Metabolites", TimeSpan.FromSeconds(517)),
        Entry(5, "Etanercept", TimeSpan.FromSeconds(440))
    ];

    /// <summary>Titles of every length and the three duration forms, to check ellipsis and column alignment.</summary>
    private static IReadOnlyList<QueueItem> LongItems(int count) =>
        Enumerable.Range(1, count)
            .Select(i => Entry(i,
                i % 3 == 0 ? $"Lecture {i}: a very long title that has to end in an ellipsis before the duration column" : $"Lecture {i}",
                i % 7 == 0 ? null : i % 5 == 0 ? TimeSpan.FromSeconds(3723) : TimeSpan.FromSeconds(60 + i * 7)))
            .ToList();

    private static QueueItem Entry(int index, string title, TimeSpan? duration) =>
        new($"vid{index:D8}", PlaylistMapping.WatchUrl($"vid{index:D8}"), title, "Armando Hasudungan", duration, index, null);

    private static ListBox Checklist(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Classes.Contains("checklist"));

    private static ListBoxItem RowItem(ListBox checklist, int index) =>
        checklist.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is QueueItemViewModel row && row.Item.Index == index);

    private static TextBlock StatusOf(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("status"));

    /// <summary>The row's ✕ (its CheckBox is a Button too).</summary>
    private static Button RemoveButton(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("chip"));

    /// <summary>Every realized row's duration ends at the same x, whatever its status, mini bar or ✕ shows (026a review note).</summary>
    private static void AssertDurationsAligned(ListBox checklist)
    {
        var rightEdges = checklist.GetVisualDescendants().OfType<ListBoxItem>()
            .Select(item => item.GetVisualDescendants().OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3))
            .Select(duration => duration.TranslatePoint(new Point(duration.Bounds.Width, 0), checklist)!.Value.X)
            .ToList();
        Assert.True(rightEdges.Count >= 3, $"Only {rightEdges.Count} rows are realized.");
        Assert.All(rightEdges, x => Assert.Equal(rightEdges[0], x, 1));
    }

    /// <summary>A real folder for the queue (it checks and creates folders) under %TEMP%, never the repo.</summary>
    private static string NewScratchFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ytdl-visual", Guid.NewGuid().ToString("N"))).FullName;

    private static void DeleteScratchFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup of the scratch folder
        }
    }

    private readonly Dictionary<IStreamInfo, string> _streamVideoIds = new();

    /// <summary>
    /// Queue mocks: SAVE TO starts at <paramref name="folder"/>, FFmpeg is ready, a fetch answers unless
    /// <paramref name="fetchError"/> gives an exception for its URL, and each stream download runs <paramref name="stream"/>
    /// with the video id it belongs to. MP3 mode: the real pipeline then converts and commits through no-op mocks.
    /// </summary>
    private void SetupQueue(string folder, Func<string, IProgress<double>?, CancellationToken, Task> stream,
        Func<string, Exception?>? fetchError = null)
    {
        _settings.Setup(s => s.Load()).Returns(new AppSettings { LastSaveFolder = folder });
        _ffmpeg.Setup(f => f.IsAvailable).Returns(true);
        _ffmpeg.Setup(f => f.EnsureAsync(It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ffmpeg.exe");
        _youtube.Setup(y => y.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((url, _) => fetchError?.Invoke(url) is { } error
                ? Task.FromException<VideoInfo>(error)
                : Task.FromResult(QueueInfo(url)));
        _youtube.Setup(y => y.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Returns<IStreamInfo, string, IProgress<double>?, CancellationToken>((source, _, progress, ct) => stream(_streamVideoIds[source], progress, ct));
    }

    /// <summary>A fetched queue item: its own audio stream, so a stream download knows which item it serves.</summary>
    private VideoInfo QueueInfo(string watchUrl)
    {
        var source = Mock.Of<IStreamInfo>();
        _streamVideoIds[source] = watchUrl[^11..];
        return SampleInfo() with { SourceUrl = watchUrl, BestAudio = new AudioStreamOption("opus", 128, "webm", source) };
    }

    /// <summary>Lets queued dispatcher work (cancellations, continuations) run until <paramref name="done"/>, 1 s at most.</summary>
    private static async Task SettleUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 50 && !done(); i++)
        {
            await Task.Delay(20);
            Settle();
        }
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
