using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public sealed class YoutubeExplodeService : IYouTubeService
{
    private readonly YoutubeClient _client = new();

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct)
    {
        var video = await _client.Videos.GetAsync(url, ct);
        var manifest = await _client.Videos.Streams.GetManifestAsync(video.Id, ct);

        var audioOptions = manifest.GetAudioOnlyStreams()
            .Select(a => StreamMapping.ToAudioOption(a.AudioCodec, a.Bitrate.BitsPerSecond, a.Container.Name, a))
            .ToList();

        if (audioOptions.Count == 0)
            throw new InvalidOperationException("This video has no downloadable audio stream.");

        var bestAudio = StreamSelector.PickBestAudio(audioOptions);
        var bestOpus = StreamSelector.PickBestOpusAudio(audioOptions);

        var videoOptions = StreamSelector.OrderVideoOptions(
            manifest.GetVideoOnlyStreams()
                .Select(v => StreamMapping.ToVideoOption(v.VideoQuality.Label, v.VideoQuality.MaxHeight, v.Container.Name, v)));

        var thumb = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";

        return new VideoInfo(
            SourceUrl: url,
            Title: video.Title,
            Author: video.Author.ChannelTitle,
            Duration: video.Duration ?? TimeSpan.Zero,
            ThumbnailUrl: thumb,
            BestAudio: bestAudio,
            VideoOptions: videoOptions,
            BestOpusAudio: bestOpus);
    }

    public async Task DownloadStreamAsync(IStreamInfo stream, string filePath, IProgress<double>? progress, CancellationToken ct)
        => await _client.Videos.Streams.DownloadAsync(stream, filePath, progress, ct);
}
