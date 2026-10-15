using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>The tags written into one queue item's file. Pure.</summary>
public static class QueueTags
{
    /// <summary>
    /// Tags for one queue item: like a single download (MainViewModel.BuildTags), but the source URL is the
    /// item's normalized watch URL. Plan 027a adds album/track.
    /// </summary>
    public static MediaTags For(QueueItem item, VideoInfo info)
        => new(info.Title, info.Author, item.Url, info.UploadDate is { Year: > 1900 } d ? d.Year : null);
}
