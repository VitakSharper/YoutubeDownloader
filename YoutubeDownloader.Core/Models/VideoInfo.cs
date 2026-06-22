namespace YoutubeDownloader.Core.Models;

public sealed record VideoInfo(
    string Title,
    string Author,
    TimeSpan Duration,
    string ThumbnailUrl,
    AudioStreamOption BestAudio,
    IReadOnlyList<VideoStreamOption> VideoOptions)
{
    public int SourceAudioKbps => BestAudio.BitrateKbps;
}
