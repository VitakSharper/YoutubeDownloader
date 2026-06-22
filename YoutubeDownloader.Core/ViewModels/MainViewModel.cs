using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IYouTubeService _youtube;
    private readonly IFFmpegLocator _ffmpeg;
    private readonly IMediaConverter _converter;
    private readonly ISaveFileService _saveFile;
    private readonly ITempFileService _temp;

    public MainViewModel(IYouTubeService youtube, IFFmpegLocator ffmpeg, IMediaConverter converter,
        ISaveFileService saveFile, ITempFileService temp)
    {
        _youtube = youtube;
        _ffmpeg = ffmpeg;
        _converter = converter;
        _saveFile = saveFile;
        _temp = temp;
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

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        var info = VideoInfo!;
        var isAudio = SelectedMode == DownloadMode.AudioMp3;
        var ext = isAudio ? "mp3" : "mp4";
        var suggested = $"{FileNameSanitizer.Sanitize(info.Title)}.{ext}";

        var target = _saveFile.PromptForSavePath(suggested, ext);
        if (target is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        var tempFiles = new List<string>();
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
            }
            else
            {
                var quality = SelectedVideoQuality ?? info.VideoOptions.First();
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
}
