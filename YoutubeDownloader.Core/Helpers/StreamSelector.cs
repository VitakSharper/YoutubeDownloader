using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

public static class StreamSelector
{
    public static AudioStreamOption PickBestAudio(IEnumerable<AudioStreamOption> audio)
    {
        var best = audio.OrderByDescending(a => a.BitrateKbps).FirstOrDefault();
        return best ?? throw new InvalidOperationException("No audio streams available.");
    }

    public static IReadOnlyList<VideoStreamOption> OrderVideoOptions(IEnumerable<VideoStreamOption> video)
        => video
            .Where(v => string.Equals(v.Container, "mp4", StringComparison.OrdinalIgnoreCase))
            .GroupBy(v => v.QualityLabel)
            .Select(g => g.First())
            .OrderByDescending(v => v.Height)
            .ToList();

    public static AudioStreamOption? PickBestOpusAudio(IEnumerable<AudioStreamOption> audio)
        => audio
            .Where(a => a.Codec.Contains("opus", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.BitrateKbps)
            .FirstOrDefault();

    /// <summary>Best AAC ("mp4a…") audio stream in an MP4 container — can be stream-copied into an .mp4.</summary>
    public static AudioStreamOption? PickBestMp4Audio(IEnumerable<AudioStreamOption> audio)
        => audio
            .Where(a => a.Codec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(a.Container, "mp4", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(a.Container, "m4a", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.BitrateKbps)
            .FirstOrDefault();
}
