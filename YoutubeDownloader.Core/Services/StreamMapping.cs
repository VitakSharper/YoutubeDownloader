using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public static class StreamMapping
{
    public static AudioStreamOption ToAudioOption(string codec, long bitsPerSecond, string container, IStreamInfo source)
        => new(codec, (int)Math.Round(bitsPerSecond / 1000.0), container, source);

    public static VideoStreamOption ToVideoOption(string qualityLabel, int height, string container, IStreamInfo source)
        => new(qualityLabel, height, container, source);
}
