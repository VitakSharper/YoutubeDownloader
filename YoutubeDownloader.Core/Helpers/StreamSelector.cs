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
}
