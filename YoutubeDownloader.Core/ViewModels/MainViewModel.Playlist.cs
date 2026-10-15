using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.ViewModels;

/// <summary>Playlist links on the Download tab: the choice banner, the playlist card and its checklist (plan 026a).</summary>
public partial class MainViewModel
{
    /// <summary>The loaded playlist card, or null in single-video mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistMode))]
    [NotifyPropertyChangedFor(nameof(ShowSingleDownload))]
    private PlaylistViewModel? _playlist;

    /// <summary>"This video / Whole playlist", offered while the URL box holds a video inside a downloadable list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylistChoice))]
    [NotifyPropertyChangedFor(nameof(PlaylistChoiceText))]
    private LinkAction.OfferChoice? _playlistChoice;

    /// <summary>One line under the URL box (Mix or account-private list fallback), or null.</summary>
    [ObservableProperty]
    private string? _linkHint;

    public bool IsPlaylistMode => Playlist is not null;
    public bool ShowSingleDownload => !IsDownloading && !IsPlaylistMode;
    public bool HasPlaylistChoice => PlaylistChoice is not null;
    public string PlaylistChoiceText => PlaylistChoice?.Kind == ListKind.Album
        ? "This link is part of an album."
        : "This link is part of a playlist.";

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
}
