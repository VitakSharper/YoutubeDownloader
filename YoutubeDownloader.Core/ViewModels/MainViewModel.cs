using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxHistory = 1000;   // maintainer answer 4 (018 §8): one ordinary playlist no longer evicts everything

    private readonly IYouTubeService _youtube;
    private readonly IFFmpegLocator _ffmpeg;
    private readonly IDownloadPipeline _pipeline;
    private readonly ISaveFileService _saveFile;
    private readonly IHistoryStore _history;
    private readonly ILinkOpener _linkOpener;
    private readonly IFileRevealer _fileRevealer;
    private readonly ISettingsStore _settings;
    private readonly IClipboardService _clipboard;
    private readonly IConfirmationService _confirm;
    private readonly IFoobar2000Launcher _foobar;
    private readonly IFolderPickerService _folderPicker;
    private readonly IRetryDelay _retryDelay;
    private readonly AppSettings _appSettings;

    public MainViewModel(IYouTubeService youtube, IFFmpegLocator ffmpeg, IDownloadPipeline pipeline,
        ISaveFileService saveFile, IHistoryStore history, ILinkOpener linkOpener,
        IFileRevealer fileRevealer, ISettingsStore settings, IClipboardService clipboard,
        IConfirmationService confirm, IFoobar2000Launcher foobar, IFolderPickerService folderPicker,
        IRetryDelay retryDelay)
    {
        _youtube = youtube;
        _ffmpeg = ffmpeg;
        _pipeline = pipeline;
        _saveFile = saveFile;
        _history = history;
        _linkOpener = linkOpener;
        _fileRevealer = fileRevealer;
        _settings = settings;
        _clipboard = clipboard;
        _confirm = confirm;
        _foobar = foobar;
        _folderPicker = folderPicker;
        _retryDelay = retryDelay;

        _appSettings = _settings.Load();
        _playlistFolder = _appSettings.LastSaveFolder;
        _alwaysOnTop = _appSettings.AlwaysOnTop;
        _selectedMode = ModeFromSetting(_appSettings.LastMode);
        _selectedQuality = Mp3Quality.FromKey(_appSettings.LastMp3Quality);

        foreach (var entry in _history.Load())
            History.Add(entry);

        History.CollectionChanged += (_, _) =>
        {
            if (!_deferHistoryGroups)   // a playlist run regroups once per batch (RecordQueueHistory)
                RebuildHistoryGroups();
        };
        RebuildHistoryGroups();
    }

    /// <summary>Past downloads, most-recent-first.</summary>
    public ObservableCollection<HistoryEntry> History { get; } = new();

    /// <summary>History filtered by <see cref="SearchText"/> and bucketed by date, for the UI.</summary>
    public ObservableCollection<HistoryGroup> HistoryGroups { get; } = new();

    /// <summary>The playlist runs the user expanded in the History tab, so a regrouping keeps them open.</summary>
    private readonly HashSet<string> _expandedRuns = new(StringComparer.Ordinal);

    public string? HistoryPlaceholder =>
        HistoryGroups.Count > 0
            ? null
            : string.IsNullOrWhiteSpace(SearchText)
                ? "No downloads yet — finished downloads will show up here."
                : $"No history matches “{SearchText.Trim()}”.";

    private void RebuildHistoryGroups()
    {
        IEnumerable<HistoryEntry> entries = History;

        var search = SearchText?.Trim();
        if (!string.IsNullOrEmpty(search))
            entries = entries.Where(e =>
                e.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(search, StringComparison.OrdinalIgnoreCase));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var searching = !string.IsNullOrEmpty(search);   // a search shows every matching run's entries
        var grouped = entries
            .GroupBy(e => DateOnly.FromDateTime(e.DownloadedAt.LocalDateTime))
            .Select(g =>
            {
                var list = g.ToList();
                return new HistoryGroup(HistoryDateLabel.For(g.Key, today), list, HistoryRows.Build(list, _expandedRuns, forceExpanded: searching));
            });

        HistoryGroups.Clear();
        foreach (var group in grouped)
            HistoryGroups.Add(group);

        OnPropertyChanged(nameof(HistoryPlaceholder));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchInfoCommand))]
    private string _url = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchInfoCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseThisVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseWholePlaylistCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadPlaylistCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangePlaylistFolderCommand))]
    [NotifyPropertyChangedFor(nameof(ShowPlaylistDownload))]
    [NotifyPropertyChangedFor(nameof(ShowRetryFailed))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDownloadCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSingleDownload))]
    [NotifyPropertyChangedFor(nameof(IsAnyDownloadActive))]
    private bool _isDownloading;

    [ObservableProperty]
    private string _statusMessage = "Paste a YouTube URL to begin.";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private VideoInfo? _videoInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    [NotifyPropertyChangedFor(nameof(ShowQuality))]
    [NotifyPropertyChangedFor(nameof(ShowVideoQuality))]
    [NotifyPropertyChangedFor(nameof(ShowAlbumTagging))]
    private DownloadMode _selectedMode = DownloadMode.AudioMp3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private Mp3Quality _selectedQuality = Mp3Quality.BestVbr;

    [ObservableProperty]
    private VideoStreamOption? _selectedVideoQuality;

    /// <summary>0 = Download tab, 1 = History tab.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Filter text for the History tab.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Keep the window above other windows (on by default).</summary>
    [ObservableProperty]
    private bool _alwaysOnTop = true;

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _appSettings.AlwaysOnTop = value;
        TrySaveSettings();
    }

    partial void OnSelectedModeChanged(DownloadMode value)
    {
        _appSettings.LastMode = value.ToString();
        TrySaveSettings();
    }

    partial void OnSelectedQualityChanged(Mp3Quality value)
    {
        // A ComboBox can transiently push null through the two-way binding; never persist that.
        if (value is null)
            return;
        _appSettings.LastMp3Quality = value.Key;
        TrySaveSettings();
    }

    private static DownloadMode ModeFromSetting(string? value)
        => Enum.TryParse<DownloadMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : DownloadMode.AudioMp3;

    /// <summary>A YouTube link detected on the clipboard, surfaced as a suggestion. Null hides the banner.</summary>
    [ObservableProperty]
    private string? _detectedClipboardUrl;

    /// <summary>The banner's text: a playlist on the clipboard is named as one.</summary>
    [ObservableProperty]
    private string _detectedClipboardMessage = "📋 Found a YouTube link on your clipboard.";

    /// <summary>The last suggestion the user used or dismissed, so it is not re-offered.</summary>
    private YouTubeLink? _dismissedLink;

    partial void OnSearchTextChanged(string value) => RebuildHistoryGroups();

    /// <summary>
    /// Follow the URL box offline: show or hide the playlist choice, drop the playlist card when the box
    /// points at another list, and drop the loaded video when it points at a different video.
    /// </summary>
    partial void OnUrlChanged(string value)
    {
        var link = YouTubeLink.Parse(value);
        if (Playlist is not null && !IsSameList(link, Playlist.ListId))
        {
            Playlist = null;
            StatusMessage = "Press Fetch to load this link.";
        }

        // The banner follows the box, offline. A card still open here is for this same list: the choice is made.
        PlaylistChoice = Playlist is null ? LinkAction.For(link) as LinkAction.OfferChoice : null;

        if (VideoInfo is null)
            return;
        if (YouTubeUrlValidator.GetVideoId(value) == YouTubeUrlValidator.GetVideoId(VideoInfo.SourceUrl))
            return; // same video, e.g. a timestamp or a different URL form
        VideoInfo = null;
        LinkHint = null;
        SelectedVideoQuality = null;
        StatusMessage = "Press Fetch to load this link.";
    }

    public IReadOnlyList<Mp3Quality> Qualities { get; } = Mp3Quality.All;
    public IReadOnlyList<DownloadMode> Modes { get; } = Enum.GetValues<DownloadMode>();

    /// <summary>
    /// The MP3 quality picker shows in MP3 mode only. Opus is a stream copy, and Video copies the AAC track
    /// (or re-encodes it at a fixed 192 kbps), so neither one reads this setting.
    /// </summary>
    public bool ShowQuality => SelectedMode == DownloadMode.AudioMp3;

    /// <summary>The video quality picker shows in Video mode only.</summary>
    public bool ShowVideoQuality => SelectedMode == DownloadMode.Video;

    /// <summary>A single video or a playlist queue is downloading. The update panel holds its install while true.</summary>
    public bool IsAnyDownloadActive => IsDownloading || IsQueueRunning;

    public string? UpscaleWarning =>
        VideoInfo is not null
        && SelectedMode == DownloadMode.AudioMp3
        && QualityAdvisor.WouldUpscale(VideoInfo.SourceAudioKbps, SelectedQuality.ApproxKbps)
            ? $"Selected {SelectedQuality.Label} is higher than the source audio " +
              $"({VideoInfo.SourceAudioKbps} kbps); this won't add real quality."
            : null;

    private bool CanFetch() => !IsBusy && !string.IsNullOrWhiteSpace(Url);
    private bool CanDownload() => !IsBusy && VideoInfo is not null;

    /// <summary>Fetch / Enter: routes the link through <see cref="LinkAction.For"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchInfoAsync()
    {
        var link = YouTubeLink.Parse(Url);
        LinkHint = null;
        switch (LinkAction.For(link))
        {
            case LinkAction.Reject reject:
                StatusMessage = reject.Message;
                return;
            case LinkAction.OpenPlaylist open:
                await LoadPlaylistAsync(open.ListId, open.Kind);
                return;
            case LinkAction.OfferChoice choice:
                PlaylistChoice = choice;            // "Fetch / Enter keeps today's meaning (this video)" (018 §2 rule 6)
                await FetchVideoAsync(PlaylistMapping.WatchUrl(choice.VideoId));
                return;
            case LinkAction.FetchVideo fetch:
                LinkHint = fetch.Hint;
                await FetchVideoAsync(link is YouTubeLink.VideoInPlaylist ? PlaylistMapping.WatchUrl(fetch.VideoId) : Url);
                return;
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>
    /// A plain video link is fetched as typed; a video taken from a list link by its canonical watch URL,
    /// so its history entry never carries <c>&amp;list=</c>.
    /// </summary>
    private async Task FetchVideoAsync(string url)
    {
        Playlist = null;
        try
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Fetching video info…";
            var info = await _youtube.GetVideoInfoAsync(url, CancellationToken.None);
            VideoInfo = info;
            SelectedVideoQuality = info.VideoOptions.FirstOrDefault();
            StatusMessage = $"Loaded: {info.Title}  •  source audio {QualityAdvisor.Describe(info.BestAudio.Codec, info.SourceAudioKbps)}";
        }
        catch (Exception ex)
        {
            VideoInfo = null;
            StatusMessage = $"Failed to load video: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Inspect the clipboard and surface a YouTube link as a suggestion when it is new.
    /// Called when the window gains focus.
    /// </summary>
    [RelayCommand]
    private void CheckClipboard()
    {
        if (IsBusy)
        {
            DetectedClipboardUrl = null;
            return;
        }

        var text = _clipboard.GetText();
        var link = YouTubeLink.Parse(text);
        if (LinkAction.For(link) is LinkAction.Reject || link == YouTubeLink.Parse(Url) || link == _dismissedLink)
        {
            DetectedClipboardUrl = null;
            return;
        }

        DetectedClipboardMessage = link is YouTubeLink.Playlist
            ? "📋 Found a YouTube playlist on your clipboard."
            : "📋 Found a YouTube link on your clipboard.";
        DetectedClipboardUrl = text;
    }

    /// <summary>Dismiss the current suggestion and suppress this same link from reappearing.</summary>
    [RelayCommand]
    private void DismissDetectedLink()
    {
        _dismissedLink = YouTubeLink.Parse(DetectedClipboardUrl);
        DetectedClipboardUrl = null;
    }

    /// <summary>Accept the suggested link: fill the box, suppress re-offering it, then fetch its info.</summary>
    [RelayCommand]
    private async Task UseDetectedLinkAsync()
    {
        var link = DetectedClipboardUrl;
        if (string.IsNullOrWhiteSpace(link))
            return;

        _dismissedLink = YouTubeLink.Parse(link);
        DetectedClipboardUrl = null;
        Url = link;

        if (FetchInfoCommand.CanExecute(null))
            await FetchInfoCommand.ExecuteAsync(null);
    }

    private CancellationTokenSource? _downloadCts;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var info = VideoInfo!;
        var mode = SelectedMode;
        var quality = SelectedQuality;
        var selectedVideoQuality = SelectedVideoQuality;

        if (mode == DownloadMode.AudioOpus && info.BestOpusAudio is null)
        {
            StatusMessage = "This video has no Opus audio stream to save.";
            return;
        }

        var ext = mode switch
        {
            DownloadMode.AudioMp3 => "mp3",
            DownloadMode.AudioOpus => "opus",
            _ => "mp4"
        };
        var suggested = $"{FileNameSanitizer.Sanitize(info.Title)}.{ext}";

        var target = await _saveFile.PromptForSavePathAsync(suggested, ext, _appSettings.LastSaveFolder);
        if (target is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        RememberSaveFolder(target);
        try
        {
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;
            IsBusy = true;
            IsDownloading = true;
            Progress = 0;
            StatusMessage = _ffmpeg.IsAvailable ? "Preparing FFmpeg…" : "Downloading FFmpeg (first run only, about 110 MB)…";
            var ffmpegPath = await _ffmpeg.EnsureAsync(
                new Progress<double>(p => Progress = p * 0.1), token);

            // Built inside the try, after FFmpeg, like today: VideoOptions.First() failing still reads "Download failed: …".
            OutputFormat format = mode switch
            {
                DownloadMode.AudioMp3 => new OutputFormat.Mp3(quality),
                DownloadMode.AudioOpus => new OutputFormat.Opus(),
                _ => new OutputFormat.Mp4(selectedVideoQuality ?? info.VideoOptions.First())
            };
            var request = new DownloadRequest(info, format, target, BuildTags(info), ffmpegPath);
            // Reports and this continuation both run on the UI thread. Once the outcome is known, a late
            // report (e.g. a last FFmpeg line read after a cancel) must not turn "Download cancelled." back into a stage.
            var acceptReports = true;
            var outcome = await _pipeline.RunAsync(request,
                new InlineProgress<PipelineProgress>(p => { if (acceptReports) ShowPipelineProgress(p, format); }), token);
            acceptReports = false;
            ShowOutcome(outcome, info);
        }
        catch (OperationCanceledException)
        {
            // Only EnsureAsync can still throw here; the pipeline returns its outcomes.
            StatusMessage = "Download cancelled.";
            Progress = 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void ShowPipelineProgress(PipelineProgress progress, OutputFormat format)
    {
        StatusMessage = PipelineStatusText.For(progress.Stage, format);
        Progress = 0.1 + progress.Fraction * 0.9;
    }

    private void ShowOutcome(DownloadOutcome outcome, VideoInfo info)
    {
        switch (outcome)
        {
            case DownloadOutcome.Saved saved:
                Progress = 1.0;
                StatusMessage = $"Done! Saved to {saved.Path}";
                RecordHistory(info.SourceUrl, info.Title, saved.FormatLabel, saved.Path);
                break;
            case DownloadOutcome.Cancelled:
                StatusMessage = "Download cancelled.";
                Progress = 0;
                break;
            case DownloadOutcome.Failed failed:
                StatusMessage = $"Download failed: {failed.Message}";
                break;
            default:
                throw new UnreachableException();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private async Task CancelDownloadAsync()
    {
        if (await _confirm.ConfirmAsync("Cancel the current download? Your progress will be lost.", "Cancel download?"))
            _downloadCts?.Cancel();
    }

    private bool CanCancelDownload() => IsDownloading;

    /// <summary>Tags written into MP3/Opus files; the source URL is the one recorded in history.</summary>
    private static MediaTags BuildTags(VideoInfo info)
        => new(info.Title, info.Author, info.SourceUrl, info.UploadDate is { Year: > 1900 } d ? d.Year : null);

    private void RememberSaveFolder(string target) => RememberFolder(Path.GetDirectoryName(target));

    /// <summary>Makes <paramref name="folder"/> the next save dialog's and folder picker's starting point.</summary>
    private void RememberFolder(string? folder)
    {
        if (!string.IsNullOrEmpty(folder) && folder != _appSettings.LastSaveFolder)
        {
            _appSettings.LastSaveFolder = folder;
            TrySaveSettings();
        }
    }

    /// <summary>Persists history; returns the error message instead of throwing.</summary>
    private string? TrySaveHistory()
    {
        try
        {
            _history.Save(History);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Persists settings; reports (never throws) on failure.</summary>
    private void TrySaveSettings()
    {
        try
        {
            _settings.Save(_appSettings);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save settings: {ex.Message}";
        }
    }

    private void RecordHistory(string url, string title, string format, string filePath)
    {
        History.Insert(0, new HistoryEntry
        {
            Url = url,
            Title = title,
            Format = format,
            FilePath = filePath,
            DownloadedAt = DateTimeOffset.Now
        });
        TrimHistory();
        if (TrySaveHistory() is { } error)
            StatusMessage = $"Done! Saved to {filePath} — but history couldn't be saved: {error}";
    }

    /// <summary>Drops the oldest entries beyond <see cref="MaxHistory"/>.</summary>
    private void TrimHistory()
    {
        while (History.Count > MaxHistory)
            History.RemoveAt(History.Count - 1);
    }

    /// <summary>Re-load a past link into the URL box, switch to the Download tab, and fetch it.</summary>
    [RelayCommand(CanExecute = nameof(CanUseEntry))]
    private async Task UseEntryAsync(HistoryEntry? entry)
    {
        if (entry is null)
            return;
        Url = entry.Url;
        SelectedTabIndex = 0;

        if (FetchInfoCommand.CanExecute(null))
            await FetchInfoCommand.ExecuteAsync(null);
    }

    private bool CanUseEntry() => !IsBusy;

    /// <summary>Open a past link in the default browser.</summary>
    [RelayCommand]
    private void OpenInBrowser(HistoryEntry? entry)
    {
        if (entry is null)
            return;
        try
        {
            _linkOpener.Open(entry.Url);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open the browser: {ex.Message}";
        }
    }

    /// <summary>Reveal the downloaded file in Explorer.</summary>
    [RelayCommand]
    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath))
            return;
        try
        {
            _fileRevealer.RevealInFolder(entry.FilePath);
        }
        catch
        {
            // best-effort: a missing/locked path shouldn't disrupt the UI
        }
    }

    public bool IsFoobarInstalled => _foobar.IsInstalled;

    public string FoobarTooltip =>
        IsFoobarInstalled ? "Open in foobar2000" : "foobar2000 is not installed";

    /// <summary>Play a downloaded file in foobar2000 (no-op unless installed).</summary>
    [RelayCommand]
    private void OpenInFoobar(HistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath) || !_foobar.IsInstalled)
            return;
        try
        {
            _foobar.Open(entry.FilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open foobar2000: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveEntry(HistoryEntry? entry)
    {
        if (entry is null || !History.Remove(entry))
            return;
        if (TrySaveHistory() is { } error)
            StatusMessage = $"Couldn't save history: {error}";
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (History.Count == 0)
            return;
        if (!await _confirm.ConfirmAsync("Clear all download history? This can't be undone.", "Clear history?"))
            return;
        History.Clear();
        if (TrySaveHistory() is { } error)
            StatusMessage = $"Couldn't save history: {error}";
    }
}
