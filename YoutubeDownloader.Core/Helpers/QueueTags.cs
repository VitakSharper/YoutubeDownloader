using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>The tags written into one queue item's file. Pure.</summary>
public static class QueueTags
{
    /// <summary>
    /// Tags for one queue item: like a single download (MainViewModel.BuildTags), but the source URL is the
    /// item's normalized watch URL and the artist drops a trailing " - Topic" / "VEVO", like the file names.
    /// With <paramref name="album"/>, the album is its title and the track is the item's playlist index (plan 027a).
    /// </summary>
    public static MediaTags For(QueueItem item, VideoInfo info, AlbumTagging? album = null)
        => new(info.Title, OutputFileNamer.ArtistFromChannel(info.Author), item.Url,
               info.UploadDate is { Year: > 1900 } d ? d.Year : null,
               Album: album?.Title, Track: album is null ? null : item.Index, TrackCount: album?.TrackCount);
}
