using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.ViewModels;

/// <summary>
/// Playlist links on the Download tab: the choice banner, the playlist card and its checklist (plan 026a),
/// and the queue run that downloads the ticked items into one folder (plan 026b).
/// </summary>
public partial class MainViewModel
{
    private const string PlaylistFolderPickerTitle = "Choose where to save the playlist";

    /// <summary>The loaded playlist card, or null in single-video mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistMode))]
    [NotifyPropertyChangedFor(nameof(ShowSingleDownload))]
    [NotifyPropertyChangedFor(nameof(ShowPlaylistDownload))]
    [NotifyPropertyChangedFor(nameof(ShowRetryFailed))]
    [NotifyPropertyChangedFor(nameof(DownloadPlaylistLabel))]
    [NotifyPropertyChangedFor(nameof(PlaylistTargetFolder))]
    [NotifyCanExecuteChangedFor(nameof(DownloadPlaylistCommand))]
    private PlaylistViewModel? _playlist;

    /// <summary>"This video / Whole playlist", offered while the URL box holds a video inside a downloadable list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylistChoice))]
    [NotifyPropertyChangedFor(nameof(PlaylistChoiceText))]
    private LinkAction.OfferChoice? _playlistChoice;

    /// <summary>One line under the URL box (Mix or account-private list fallback), or null.</summary>
    [ObservableProperty]
    private string? _linkHint;

    /// <summary>The parent folder the queue saves into; starts at <see cref="AppSettings.LastSaveFolder"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaylistTargetFolder))]
    private string _playlistFolder = "";

    /// <summary>Maintainer answer 3: a sub-folder named after the playlist, on by default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaylistTargetFolder))]
    private bool _useSubFolder = true;

    public IReadOnlyList<VideoHeightCap> HeightCaps { get; } = VideoHeightCap.All;

    /// <summary>Maintainer answer 11: 1080p (H.264) by default. A ComboBox can push null: treat it as the default.</summary>
    [ObservableProperty]
    private VideoHeightCap? _selectedHeightCap = VideoHeightCap.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaylistDownload))]
    [NotifyPropertyChangedFor(nameof(ShowRetryFailed))]
    [NotifyCanExecuteChangedFor(nameof(CancelAllCommand))]
    private bool _isQueueRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRetryFailed))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    private bool _hasRetryableItems;

    public bool IsPlaylistMode => Playlist is not null;
    public bool ShowSingleDownload => !IsDownloading && !IsPlaylistMode;
    public bool HasPlaylistChoice => PlaylistChoice is not null;
    public string PlaylistChoiceText => PlaylistChoice?.Kind == ListKind.Album
        ? "This link is part of an album."
        : "This link is part of a playlist.";

    public bool ShowPlaylistDownload => IsPlaylistMode && !IsQueueRunning;
    public bool ShowRetryFailed => IsPlaylistMode && HasRetryableItems && !IsQueueRunning;
    public string DownloadPlaylistLabel => $"Download {Playlist?.RunnableCount ?? 0}";

    /// <summary>Where the files go: "C:\Music\Pharmacology" with the sub-folder on.</summary>
    public string PlaylistTargetFolder =>
        string.IsNullOrEmpty(PlaylistFolder) ? "Chosen when you start"
        : UseSubFolder && Playlist is not null ? OutputFileNamer.SubFolder(PlaylistFolder, Playlist.Title)
        : PlaylistFolder;

    private CancellationTokenSource? _queueCts;
    private PlaylistQueueRunner? _queueRunner;
    /// <summary>The whole run, cleanup included: what <see cref="StopQueueAsync"/> waits for.</summary>
    private Task? _queueRun;
    private QueueRunCounters? _queueCounters;

    /// <summary>Counts for the overall bar and the status line of the current run.</summary>
    private sealed class QueueRunCounters
    {
        public required int Total { get; init; }
        public int Done { get; set; }
        public int Saved { get; set; }
        public double CurrentFraction { get; set; }
    }

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private async Task ChooseThisVideoAsync()
    {
        var choice = PlaylistChoice;
        PlaylistChoice = null;
        if (choice is null)
            return;
        if (VideoInfo is null || YouTubeUrlValidator.GetVideoId(VideoInfo.SourceUrl) != choice.VideoId)
            await FetchVideoAsync(PlaylistMapping.WatchUrl(choice.VideoId));
    }

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private async Task ChooseWholePlaylistAsync()
    {
        if (PlaylistChoice is { } choice)
            await LoadPlaylistAsync(choice.ListId, choice.Kind);
    }

    private bool CanChoose() => !IsBusy;

    /// <summary>Replaces the video card with the playlist card and fills its checklist page by page.</summary>
    private async Task LoadPlaylistAsync(string listId, ListKind kind)
    {
        VideoInfo = null;
        SelectedVideoQuality = null;
        PlaylistChoice = null;
        LinkHint = null;
        var playlist = new PlaylistViewModel(listId, kind);
        Playlist = playlist;
        try
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Loading playlist…";
            playlist.Info = await _youtube.GetPlaylistAsync(listId, CancellationToken.None);
            await foreach (var batch in _youtube.GetPlaylistItemBatchesAsync(listId, CancellationToken.None))
            {
                playlist.AddItems(batch);
                StatusMessage = $"Loading playlist… ({playlist.Items.Count} so far)";
            }
            playlist.FinishLoading();
            StatusMessage = playlist.Items.Count == 0
                ? "This playlist has no videos."
                : $"Loaded: {playlist.Title} · {PlaylistViewModel.VideoCount(playlist.Items.Count)}";
        }
        catch (PlaylistUnavailableException)
        {
            Playlist = null;
            StatusMessage = "This playlist is private or doesn't exist.";   // 018 §9
        }
        catch (Exception ex)
        {
            Playlist = null;
            StatusMessage = $"Failed to load playlist: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsSameList(YouTubeLink link, string listId)
        => link is YouTubeLink.Playlist p && p.ListId == listId
           || link is YouTubeLink.VideoInPlaylist v && v.ListId == listId;

    /// <summary>Follows the open card: its title, loading state and ticked count feed the folder line and "Download N".</summary>
    partial void OnPlaylistChanged(PlaylistViewModel? oldValue, PlaylistViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnPlaylistCardChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnPlaylistCardChanged;
        HasRetryableItems = false;
    }

    private void OnPlaylistCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PlaylistViewModel.RunnableCount) or nameof(PlaylistViewModel.IsLoading) or nameof(PlaylistViewModel.Title)))
            return;
        OnPropertyChanged(nameof(DownloadPlaylistLabel));
        OnPropertyChanged(nameof(PlaylistTargetFolder));
        DownloadPlaylistCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Download N": every ticked item not saved yet.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadPlaylist))]
    private Task DownloadPlaylistAsync()
        => StartQueue(Playlist!.Items.Where(r => r.IsSelected && r.State is not ItemState.Saved).ToList());

    private bool CanDownloadPlaylist() => !IsBusy && Playlist is { IsLoading: false, RunnableCount: > 0 };

    /// <summary>Maintainer answer 13: only the failed (and cancelled) items, manifests fetched again.</summary>
    [RelayCommand(CanExecute = nameof(CanRetryFailed))]
    private Task RetryFailedAsync()
    {
        var rows = Playlist!.Items.Where(r => r.IsSelected && r.State is ItemState.Failed or ItemState.Cancelled).ToList();
        if (rows.Count == 0)
        {
            StatusMessage = "Tick a failed item to retry it.";
            return Task.CompletedTask;
        }
        return StartQueue(rows);
    }

    private bool CanRetryFailed() => !IsBusy && HasRetryableItems;

    [RelayCommand(CanExecute = nameof(IsQueueRunning))]
    private async Task CancelAllAsync()
    {
        var remaining = Playlist?.Items.Count(r => r.IsQueued && !r.State.IsFinal) ?? 0;
        var question = remaining == 1
            ? "Cancel the remaining download? Files already saved are kept."
            : $"Cancel the remaining {remaining} downloads? Files already saved are kept.";
        if (await _confirm.ConfirmAsync(question, "Cancel all?"))
            _queueCts?.Cancel();
    }

    /// <summary>The row's ✕: no confirmation (maintainer answer 12).</summary>
    [RelayCommand]
    private void RemoveQueueItem(QueueItemViewModel? row)
    {
        if (row is not null)
            _queueRunner?.CancelItem(row.Item.VideoId);
    }

    [RelayCommand(CanExecute = nameof(CanChangeFolder))]
    private async Task ChangePlaylistFolderAsync()
    {
        var picked = await _folderPicker.PickFolderAsync(PlaylistFolderPickerTitle,
            string.IsNullOrEmpty(PlaylistFolder) ? _appSettings.LastSaveFolder : PlaylistFolder);
        if (picked is null)
            return;
        PlaylistFolder = picked;
        RememberFolder(picked);
    }

    private bool CanChangeFolder() => !IsBusy;

    /// <summary>For window close: cancel without asking, then wait up to <paramref name="timeout"/> for the run to clean up.</summary>
    public async Task StopQueueAsync(TimeSpan timeout)
    {
        _queueCts?.Cancel();
        if (_queueRun is { } run)
            await Task.WhenAny(run, Task.Delay(timeout));
    }

    private Task StartQueue(IReadOnlyList<QueueItemViewModel> rows) => _queueRun = RunQueueAsync(rows);

    /// <summary>One run over <paramref name="rows"/>. The edges live here (folder picker, file checks); the decisions in pure helpers.</summary>
    private async Task RunQueueAsync(IReadOnlyList<QueueItemViewModel> rows)
    {
        var playlist = Playlist!;
        var parent = await ResolvePlaylistFolderAsync();
        if (parent is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        var folder = UseSubFolder ? OutputFileNamer.SubFolder(parent, playlist.Title) : parent;
        QueueFormat format = SelectedMode switch
        {
            DownloadMode.AudioMp3 => new QueueFormat.Mp3(SelectedQuality),
            DownloadMode.AudioOpus => new QueueFormat.Opus(),
            _ => new QueueFormat.Mp4((SelectedHeightCap ?? VideoHeightCap.Default).MaxHeight)
        };
        var extension = QueueFormatResolver.Extension(format);

        // Maintainer answer 9: an item history says was saved here (same video, folder, extension, file still there) is skipped.
        var alreadySaved = OutputFileNamer.AlreadySavedVideoIds(History, folder, extension, File.Exists);
        foreach (var row in rows.Where(r => alreadySaved.Contains(r.Item.VideoId)))
            row.State = new ItemState.Skipped("Already saved");
        var toRun = rows.Where(r => !alreadySaved.Contains(r.Item.VideoId)).ToList();
        if (toRun.Count == 0)
        {
            StatusMessage = "Everything selected is already saved in this folder.";
            playlist.RefreshCounts();
            return;
        }

        var targets = OutputFileNamer.Plan(
            toRun.Select(r => new NamingEntry(r.Item.Index, r.Item.Title, r.Item.Author)).ToList(),
            folder, extension, File.Exists);

        try
        {
            _queueRunId = Guid.NewGuid().ToString("N");   // one per run: a "Retry failed" gets its own History sub-header
            _queueCts = new CancellationTokenSource();
            var token = _queueCts.Token;
            IsBusy = true;
            IsQueueRunning = true;
            playlist.HasStartedRun = true;
            DetectedClipboardUrl = null;   // its "Use it" would replace the card mid-run; CheckClipboard keeps it hidden while busy
            Progress = 0;
            _queueCounters = new QueueRunCounters { Total = rows.Count, Done = rows.Count - toRun.Count };
            foreach (var row in toRun)
            {
                row.State = new ItemState.Pending();
                row.IsQueued = true;
            }

            // 018 §6: FFmpeg is ensured once per queue, not per item.
            StatusMessage = _ffmpeg.IsAvailable ? "Preparing FFmpeg…" : "Downloading FFmpeg (first run only, about 110 MB)…";
            var ffmpegPath = await _ffmpeg.EnsureAsync(new Progress<double>(p => Progress = p * 0.1), token);
            Directory.CreateDirectory(folder);
            StatusMessage = "Fetching video info…";

            var job = new QueueJob(toRun.Select((row, i) => new QueueJobItem(row.Item, targets[i])).ToList(), format, ffmpegPath);
            _queueRunner = new PlaylistQueueRunner(_youtube, _pipeline, _retryDelay);
            var stop = await _queueRunner.RunAsync(job, new InlineProgress<QueueEvent>(OnQueueEvent), token);
            StatusMessage = StopText(stop) + QueueSummary.From(rows.Select(r => r.State).ToList()).Describe();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";          // cancelled while FFmpeg was being prepared
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";   // FFmpeg bootstrap or the folder could not be created
        }
        finally
        {
            foreach (var row in toRun)
                row.IsQueued = false;
            IsBusy = false;
            IsQueueRunning = false;
            _queueRunner = null;
            _queueCounters = null;
            _queueCts?.Dispose();
            _queueCts = null;
            HasRetryableItems = playlist.Items.Any(r => r.State is ItemState.Failed or ItemState.Cancelled);
            playlist.RefreshCounts();
            FlushHistory();   // the run's last entries (fewer than HistoryFlushEvery)
        }
    }

    /// <summary>
    /// The SAVE TO folder when it still exists; otherwise asks once, starting at the last save folder. The pick
    /// (the parent, not the playlist sub-folder) becomes the new last save folder. Null when the user cancels.
    /// </summary>
    private async Task<string?> ResolvePlaylistFolderAsync()
    {
        if (!string.IsNullOrEmpty(PlaylistFolder) && Directory.Exists(PlaylistFolder))
            return PlaylistFolder;

        var picked = await _folderPicker.PickFolderAsync(PlaylistFolderPickerTitle, _appSettings.LastSaveFolder);
        if (picked is null)
            return null;
        PlaylistFolder = picked;
        RememberFolder(picked);
        return picked;
    }

    private static string StopText(QueueStop stop) => stop switch
    {
        QueueStop.Completed => "",
        QueueStop.Cancelled => "Cancelled. ",
        QueueStop.RateLimited => "YouTube is rate-limiting — resume later. ",
        QueueStop.DiskFull => "The disk is full: the queue stopped. ",
        _ => throw new UnreachableException()
    };

    /// <summary>Runner reports, applied where they are reported (the UI thread in the app, through <see cref="InlineProgress{T}"/>).</summary>
    private void OnQueueEvent(QueueEvent queueEvent)
    {
        switch (queueEvent)
        {
            case QueueEvent.RateLimitPause pause:
                StatusMessage = $"YouTube is rate-limiting — waiting {pause.Wait.TotalSeconds:0} s…";
                break;
            case QueueEvent.ItemChanged changed:
                ApplyItemChange(changed.Item, changed.State);
                break;
            default:
                throw new UnreachableException();
        }
    }

    private void ApplyItemChange(QueueItem item, ItemState state)
    {
        if (Playlist?.Find(item.VideoId) is not { } row || _queueCounters is not { } counters)
            return;

        var previous = row.State;
        row.State = state;
        if (state.IsFinal && !previous.IsFinal)
        {
            counters.Done++;
            if (previous is ItemState.Downloading)
                counters.CurrentFraction = 0;
        }

        switch (state)
        {
            case ItemState.Downloading downloading:
                counters.CurrentFraction = downloading.Progress;
                StatusMessage = $"{counters.Saved} of {counters.Total} saved · downloading {row.NumberText}. {item.Title}";
                break;
            case ItemState.Saved saved:
                counters.Saved++;
                // One entry per item, canonical watch URL (018 §8), tagged with its run for the History tab.
                RecordQueueHistory(new HistoryEntry
                {
                    Url = item.Url,
                    Title = item.Title,
                    Format = saved.FormatLabel,
                    FilePath = saved.Path,
                    DownloadedAt = DateTimeOffset.Now,
                    VideoId = item.VideoId,
                    PlaylistId = Playlist!.ListId,
                    PlaylistTitle = Playlist.Title,
                    RunId = _queueRunId,
                    TrackIndex = item.Index
                });
                break;
        }

        // The single download's windows: 0-10 % FFmpeg, then the items done plus the current item's fraction.
        Progress = 0.1 + 0.9 * Math.Min(1.0, (counters.Done + counters.CurrentFraction) / counters.Total);
    }

    /// <summary>Queue entries are regrouped and saved once per this many, at the end of the run, and when the History tab is opened.</summary>
    internal const int HistoryFlushEvery = 10;

    /// <summary>Set at the start of each run; groups that run's entries under one History sub-header.</summary>
    private string? _queueRunId;

    private bool _deferHistoryGroups;
    private int _unflushedQueueEntries;

    /// <summary>
    /// Adds one queue entry without regrouping the History tab or rewriting history.json each time (018 §8).
    /// Deterministic on purpose: no SynchronizationContext post (xUnit 2's async test context posts to the
    /// thread pool, which would race with the view model in the queue tests).
    /// </summary>
    internal void RecordQueueHistory(HistoryEntry entry)
    {
        _deferHistoryGroups = true;
        try
        {
            History.Insert(0, entry);
            TrimHistory();
        }
        finally
        {
            _deferHistoryGroups = false;
        }
        if (++_unflushedQueueEntries >= HistoryFlushEvery || SelectedTabIndex == 1)
            FlushHistory();
    }

    /// <summary>Regroups and saves the queue entries recorded since the last flush; does nothing when there are none.</summary>
    private void FlushHistory()
    {
        if (_unflushedQueueEntries == 0)
            return;
        _unflushedQueueEntries = 0;
        RebuildHistoryGroups();
        if (TrySaveHistory() is { } error)
            StatusMessage = $"Couldn't save history: {error}";
    }

    /// <summary>Opening the History tab during a run shows the entries saved so far.</summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1)
            FlushHistory();
    }
}
