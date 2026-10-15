namespace YoutubeDownloader.Core.Models;

/// <param name="SourceUrl">The URL this info was fetched from; recorded in history.</param>
public sealed record VideoInfo(
    string SourceUrl,
    string Title,
    string Author,
    TimeSpan Duration,
    string ThumbnailUrl,
    AudioStreamOption BestAudio,
    IReadOnlyList<VideoStreamOption> VideoOptions,
    AudioStreamOption? BestOpusAudio = null,
    AudioStreamOption? BestMp4Audio = null)
{
    public int SourceAudioKbps => BestAudio.BitrateKbps;
}
