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
    private UpdateInfo? _latest;

    public UpdateViewModel(IUpdateService updates, IFileRevealer revealer)
    {
        _updates = updates;
        _revealer = revealer;
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

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
            else if (info.IsUpdateAvailable && !string.IsNullOrEmpty(info.DownloadUrl))
            {
                IsUpdateAvailable = true;
                Status = $"Version {info.LatestVersion} is available (you have {info.CurrentVersion}).";
            }
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

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsBusy || _latest is null || !IsUpdateAvailable)
            return;

        IsBusy = true;
        Progress = 0;
        Status = "Downloading…";
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            var exe = await _updates.DownloadAndExtractAsync(_latest, progress);
            _revealer.RevealInFolder(exe);
            Status = "Downloaded. Close the app, then replace the old .exe with the new one.";
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

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Update check is rate-limited — try again in a few minutes.",
        HttpRequestException => "Couldn't reach GitHub. Check your connection.",
        _ => "Update failed — please try again.",
    };
}
