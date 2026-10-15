using System.Diagnostics;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>Turns the queue's format into one item's output format. Pure.</summary>
public static class QueueFormatResolver
{
    /// <summary>
    /// The per-item output format, or null when this item cannot be saved in the queue's format
    /// (no Opus stream, or no MP4 video at all). The queue shows no MP3 upscale warning (018 §4).
    /// </summary>
    public static OutputFormat? Resolve(QueueFormat format, VideoInfo info) => format switch
    {
        QueueFormat.Mp3 mp3 => new OutputFormat.Mp3(mp3.Quality),
        QueueFormat.Opus => info.BestOpusAudio is null ? null : new OutputFormat.Opus(),
        QueueFormat.Mp4 mp4 => PickVideo(info.VideoOptions, mp4.MaxHeight) is { } video ? new OutputFormat.Mp4(video) : null,
        _ => throw new UnreachableException()
    };

    /// <summary>The tallest option not above the cap; if none fits, the smallest one; null when there are none.</summary>
    public static VideoStreamOption? PickVideo(IReadOnlyList<VideoStreamOption> options, int maxHeight)
        => options.Where(o => o.Height <= maxHeight).MaxBy(o => o.Height) ?? options.MinBy(o => o.Height);

    /// <summary>The file extension, without a dot, that every item of this queue is saved with.</summary>
    public static string Extension(QueueFormat format) => format switch
    {
        QueueFormat.Mp3 => "mp3",
        QueueFormat.Opus => "opus",
        QueueFormat.Mp4 => "mp4",
        _ => throw new UnreachableException()
    };
}
