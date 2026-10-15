using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

public static class StreamSelector
{
    public static AudioStreamOption PickBestAudio(IEnumerable<AudioStreamOption> audio)
    {
        var best = PreferOriginalLanguage(audio).OrderByDescending(a => a.BitrateKbps).FirstOrDefault();
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
        => PreferOriginalLanguage(audio.Where(a => a.Codec.Contains("opus", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.BitrateKbps)
            .FirstOrDefault();

    /// <summary>Best AAC ("mp4a…") audio stream in an MP4 container — can be stream-copied into an .mp4.</summary>
    public static AudioStreamOption? PickBestMp4Audio(IEnumerable<AudioStreamOption> audio)
        => PreferOriginalLanguage(audio
                .Where(a => a.Codec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(a.Container, "mp4", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(a.Container, "m4a", StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(a => a.BitrateKbps)
            .FirstOrDefault();

    /// <summary>
    /// Within one picker's candidates: only the original-language tracks when the manifest flags at least one,
    /// otherwise every candidate unchanged. Applied per picker, never to the whole list up front, so a codec whose
    /// original track is missing still finds its best stream instead of falling back to a re-encode.
    /// </summary>
    private static IReadOnlyList<AudioStreamOption> PreferOriginalLanguage(IEnumerable<AudioStreamOption> candidates)
    {
        var all = candidates.ToList();
        var original = all.Where(a => a.IsOriginalLanguage == true).ToList();
        return original.Count > 0 ? original : all;
    }
}
