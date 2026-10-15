using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public static class PlaylistMapping
{
    /// <summary>The canonical single-video link: no list, no index, no timestamp.</summary>
    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    public static QueueItem ToQueueItem(PlaylistVideo video, int index, int thumbnailMinWidth)
        => new(
            VideoId: video.Id.Value,
            Url: WatchUrl(video.Id.Value),
            Title: video.Title,
            Author: video.Author.ChannelTitle,
            Duration: video.Duration,
            Index: index,
            ThumbnailUrl: Thumbnail(video.Thumbnails, thumbnailMinWidth));

    public static PlaylistInfo ToPlaylistInfo(Playlist playlist, int thumbnailMinWidth)
        => new(
            ListId: playlist.Id.Value,
            Title: playlist.Title,
            Author: playlist.Author?.ChannelTitle,
            Count: playlist.Count,
            ThumbnailUrl: Thumbnail(playlist.Thumbnails, thumbnailMinWidth));

    private static string? Thumbnail(IEnumerable<Thumbnail> thumbnails, int minWidth)
    {
        var url = ThumbnailPicker.Pick(thumbnails.Select(t => (t.Url, t.Resolution.Width, t.Resolution.Height)), minWidth);
        return url.Length == 0 ? null : url;
    }
}
