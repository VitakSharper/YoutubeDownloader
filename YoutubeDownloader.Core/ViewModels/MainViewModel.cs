using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxHistory = 100;

    private readonly IYouTubeService _youtube;
    private readonly IFFmpegLocator _ffmpeg;
    private readonly IMediaConverter _converter;
    private readonly ISaveFileService _saveFile;
    private readonly ITempFileService _temp;
    private readonly IHistoryStore _history;
    private readonly ILinkOpener _linkOpener;
    private readonly IFileRevealer _fileRevealer;
    private readonly ISettingsStore _settings;
    private readonly IClipboardService _clipboard;
    private readonly AppSettings _appSettings;

    public MainViewModel(IYouTubeService youtube, IFFmpegLocator ffmpeg, IMediaConverter converter,
        ISaveFileService saveFile, ITempFileService temp, IHistoryStore history, ILinkOpener linkOpener,
        IFileRevealer fileRevealer, ISettingsStore settings, IClipboardService clipboard)
    {
        _youtube = youtube;
        _ffmpeg = ffmpeg;
        _converter = converter;
        _saveFile = saveFile;
        _temp = temp;
        _history = history;
        _linkOpener = linkOpener;
        _fileRevealer = fileRevealer;
        _settings = settings;
        _clipboard = clipboard;

        _appSettings = _settings.Load();
        _alwaysOnTop = _appSettings.AlwaysOnTop;

        foreach (var entry in _history.Load())
            History.Add(entry);

        History.CollectionChanged += (_, _) => RebuildHistoryGroups();
        RebuildHistoryGroups();
    }

    /// <summary>Past downloads, most-recent-first.</summary>
    public ObservableCollection<HistoryEntry> History { get; } = new();

    /// <summary>History filtered by <see cref="SearchText"/> and bucketed by date, for the UI.</summary>
    public ObservableCollection<HistoryGroup> HistoryGroups { get; } = new();

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
        var grouped = entries
            .GroupBy(e => DateOnly.FromDateTime(e.DownloadedAt.LocalDateTime))
            .Select(g => new HistoryGroup(HistoryDateLabel.For(g.Key, today), g.ToList()));

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
    private bool _isBusy;

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
    private DownloadMode _selectedMode = DownloadMode.AudioMp3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleWarning))]
    private Mp3Bitrate _selectedBitrate = Mp3Bitrate.Kbps192;

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
        _settings.Save(_appSettings);
    }

    /// <summary>A YouTube link detected on the clipboard, surfaced as a suggestion. Null hides the banner.</summary>
    [ObservableProperty]
    private string? _detectedClipboardUrl;

    /// <summary>Video id of the last suggestion the user used or dismissed, so it is not re-offered.</summary>
    private string? _dismissedVideoId;

    partial void OnSearchTextChanged(string value) => RebuildHistoryGroups();

    public IReadOnlyList<Mp3Bitrate> Bitrates { get; } = Enum.GetValues<Mp3Bitrate>();
    public IReadOnlyList<DownloadMode> Modes { get; } = Enum.GetValues<DownloadMode>();

    public string? UpscaleWarning =>
        VideoInfo is not null
        && SelectedMode == DownloadMode.AudioMp3
        && QualityAdvisor.WouldUpscale(VideoInfo.SourceAudioKbps, (int)SelectedBitrate)
            ? $"Selected {(int)SelectedBitrate} kbps is higher than the source audio " +
              $"({VideoInfo.SourceAudioKbps} kbps); this won't add real quality."
            : null;

    private bool CanFetch() => !IsBusy && !string.IsNullOrWhiteSpace(Url);
    private bool CanDownload() => !IsBusy && VideoInfo is not null;

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchInfoAsync()
    {
        if (!YouTubeUrlValidator.IsValid(Url))
        {
            StatusMessage = "That doesn't look like a valid YouTube URL.";
            return;
        }

        try
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Fetching video info…";
            var info = await _youtube.GetVideoInfoAsync(Url, CancellationToken.None);
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
        var videoId = YouTubeUrlValidator.GetVideoId(text);

        if (videoId is null
            || videoId == YouTubeUrlValidator.GetVideoId(Url)
            || videoId == _dismissedVideoId)
        {
            DetectedClipboardUrl = null;
            return;
        }

        DetectedClipboardUrl = text;
    }

    /// <summary>Dismiss the current suggestion and suppress this same link from reappearing.</summary>
    [RelayCommand]
    private void DismissDetectedLink()
    {
        _dismissedVideoId = YouTubeUrlValidator.GetVideoId(DetectedClipboardUrl);
        DetectedClipboardUrl = null;
    }

    /// <summary>Accept the suggested link: fill the box, suppress re-offering it, then fetch its info.</summary>
    [RelayCommand]
    private async Task UseDetectedLinkAsync()
    {
        var link = DetectedClipboardUrl;
        if (string.IsNullOrWhiteSpace(link))
            return;

        _dismissedVideoId = YouTubeUrlValidator.GetVideoId(link);
        DetectedClipboardUrl = null;
        Url = link;

        if (FetchInfoCommand.CanExecute(null))
            await FetchInfoCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var info = VideoInfo!;
        var isAudio = SelectedMode == DownloadMode.AudioMp3;
        var ext = isAudio ? "mp3" : "mp4";
        var suggested = $"{FileNameSanitizer.Sanitize(info.Title)}.{ext}";

        var target = _saveFile.PromptForSavePath(suggested, ext, _appSettings.LastSaveFolder);
        if (target is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        RememberSaveFolder(target);

        var tempFiles = new List<string>();
        string formatLabel = "";
        try
        {
            IsBusy = true;
            Progress = 0;
            StatusMessage = "Preparing FFmpeg…";
            var ffmpegPath = await _ffmpeg.EnsureAsync(
                new Progress<double>(p => Progress = p * 0.1), CancellationToken.None);

            if (isAudio)
            {
                StatusMessage = "Downloading audio…";
                var tempAudio = _temp.NewTempFile(info.BestAudio.Container);
                tempFiles.Add(tempAudio);
                await _youtube.DownloadStreamAsync(info.BestAudio.Source, tempAudio,
                    new Progress<double>(p => Progress = 0.1 + p * 0.6), CancellationToken.None);

                StatusMessage = $"Converting to MP3 ({(int)SelectedBitrate} kbps)…";
                await _converter.ConvertToMp3Async(ffmpegPath, tempAudio, target, (int)SelectedBitrate,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), CancellationToken.None);
                formatLabel = $"MP3 · {(int)SelectedBitrate} kbps";
            }
            else
            {
                var quality = SelectedVideoQuality ?? info.VideoOptions.First();
                formatLabel = $"MP4 · {quality.QualityLabel}";
                StatusMessage = $"Downloading video ({quality.QualityLabel})…";
                var tempVideo = _temp.NewTempFile(quality.Container);
                tempFiles.Add(tempVideo);
                var tempAudio = _temp.NewTempFile(info.BestAudio.Container);
                tempFiles.Add(tempAudio);

                await _youtube.DownloadStreamAsync(quality.Source, tempVideo,
                    new Progress<double>(p => Progress = 0.1 + p * 0.4), CancellationToken.None);
                await _youtube.DownloadStreamAsync(info.BestAudio.Source, tempAudio,
                    new Progress<double>(p => Progress = 0.5 + p * 0.2), CancellationToken.None);

                StatusMessage = "Merging video + audio…";
                await _converter.MuxToMp4Async(ffmpegPath, tempVideo, tempAudio, target,
                    info.Duration, new Progress<double>(p => Progress = 0.7 + p * 0.3), CancellationToken.None);
            }

            Progress = 1.0;
            StatusMessage = $"Done! Saved to {target}";
            RecordHistory(info.Title, formatLabel, target);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            foreach (var f in tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); }
                catch { /* best-effort temp cleanup */ }
            }
        }
    }

    private void RememberSaveFolder(string target)
    {
        var folder = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(folder) && folder != _appSettings.LastSaveFolder)
        {
            _appSettings.LastSaveFolder = folder;
            _settings.Save(_appSettings);
        }
    }

    private void RecordHistory(string title, string format, string filePath)
    {
        History.Insert(0, new HistoryEntry
        {
            Url = Url,
            Title = title,
            Format = format,
            FilePath = filePath,
            DownloadedAt = DateTimeOffset.Now
        });
        while (History.Count > MaxHistory)
            History.RemoveAt(History.Count - 1);
        _history.Save(History);
    }

    /// <summary>Re-load a past link into the URL box and switch to the Download tab.</summary>
    [RelayCommand]
    private void UseEntry(HistoryEntry? entry)
    {
        if (entry is null)
            return;
        Url = entry.Url;
        SelectedTabIndex = 0;
    }

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

    [RelayCommand]
    private void RemoveEntry(HistoryEntry? entry)
    {
        if (entry is null || !History.Remove(entry))
            return;
        _history.Save(History);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (History.Count == 0)
            return;
        History.Clear();
        _history.Save(History);
    }
}
