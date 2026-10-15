using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IFileRevealer _revealer;
    private readonly ISelfUpdater _selfUpdater;
    private UpdateInfo? _latest;

    public UpdateViewModel(IUpdateService updates, IFileRevealer revealer, ISelfUpdater selfUpdater)
    {
        _updates = updates;
        _revealer = revealer;
        _selfUpdater = selfUpdater;
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanStartUpdateDownload))] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isDownloadingUpdate;

    /// <summary>
    /// A video or playlist download is running in the main window. Installing an update restarts the app,
    /// which would kill that download, so the update's Download waits until it is over.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanStartUpdateDownload)), NotifyPropertyChangedFor(nameof(DownloadUpdateToolTip))]
    private bool _isAppDownloading;

    public bool CanStartUpdateDownload => !IsBusy && !IsAppDownloading;

    public string? DownloadUpdateToolTip => IsAppDownloading ? WaitForDownloadsHint : null;

    private const string WaitForDownloadsHint = "Finish or cancel the current download first.";

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsUpdateAvailable = false;
        Status = "Checking for updates…";
        try
        {
            var info = await _updates.CheckAsync();
            _latest = info;

            if (info.LatestVersion is null)
                Status = "No releases have been published yet.";
            else if (IsDownloadable(info))
                ShowAvailable(info);
            else if (info.IsUpdateAvailable)
                Status = $"Version {info.LatestVersion} is available, but the release has no downloadable file.";
            else
                Status = $"You're on the latest version ({info.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// One quiet check, run at startup: it only speaks up when a downloadable update exists.
    /// Up to date, no release, no asset or any error leave <see cref="Status"/> untouched. Never throws.
    /// </summary>
    public async Task CheckSilentlyAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var info = await _updates.CheckAsync();
            _latest = info;

            if (IsDownloadable(info))
                ShowAvailable(info);
        }
        catch (Exception)
        {
            // Silent by design: no error noise at startup. The manual check still reports errors.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsDownloadable(UpdateInfo info) =>
        info.IsUpdateAvailable && !string.IsNullOrEmpty(info.DownloadUrl);

    private void ShowAvailable(UpdateInfo info)
    {
        IsUpdateAvailable = true;
        Status = $"Version {info.LatestVersion} is available (you have {info.CurrentVersion}).";
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (!CanStartUpdateDownload || _latest is null || !IsUpdateAvailable)
            return;

        IsBusy = true;
        Progress = 0;
        IsDownloadingUpdate = true;
        Status = "Downloading…";
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            var exe = await _updates.DownloadAndExtractAsync(_latest, progress);
            if (TryInstallAndRestart(exe))
                return;

            _revealer.RevealInFolder(exe);
            // A download started while the update was downloading: restarting now would kill it.
            Status = IsAppDownloading
                ? "Downloaded, but not installed while a download is running. When it's done, close the app and replace the old .exe with the new one."
                : "Downloaded. Close the app, then replace the old .exe with the new one.";
        }
        catch (Exception ex)
        {
            Status = FriendlyError(ex);
        }
        finally
        {
            IsDownloadingUpdate = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Replaces the running exe with <paramref name="exe"/> and restarts, when this build can.
    /// False (manual flow) when a download is running, it is not an exe, the build is not eligible, or the install fails.
    /// </summary>
    private bool TryInstallAndRestart(string exe)
    {
        if (IsAppDownloading || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !_selfUpdater.CanSelfUpdate)
            return false;

        Status = "Installing update…";
        try
        {
            _selfUpdater.ApplyAndRestart(exe);
            return true;
        }
        catch (Exception)
        {
            return false; // e.g. the previous update's .old is still locked: nothing changed, fall back
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "Update check is rate-limited — try again in a few minutes.",
        HttpRequestException => "Couldn't reach GitHub. Check your connection.",
        _ => "Update failed — please try again.",
    };
}
