namespace YoutubeDownloader.Core.Models;

/// <param name="SourceUrl">The URL this info was fetched from; recorded in history.</param>
/// <param name="ThumbnailUrl">A small thumbnail for the UI.</param>
/// <param name="UploadDate">When the video was published; its year is written as the date tag.</param>
/// <param name="CoverArtUrl">The highest-resolution thumbnail, embedded as MP3 cover art.</param>
public sealed record VideoInfo(
    string SourceUrl,
    string Title,
    string Author,
    TimeSpan Duration,
    string ThumbnailUrl,
    AudioStreamOption BestAudio,
    IReadOnlyList<VideoStreamOption> VideoOptions,
    AudioStreamOption? BestOpusAudio = null,
    AudioStreamOption? BestMp4Audio = null,
    DateTimeOffset? UploadDate = null,
    string? CoverArtUrl = null)
{
    public int SourceAudioKbps => BestAudio.BitrateKbps;
}
