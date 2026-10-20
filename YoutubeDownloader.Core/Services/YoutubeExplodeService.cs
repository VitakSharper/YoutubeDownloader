using System.Net;
using System.Runtime.CompilerServices;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public sealed class YoutubeExplodeService : IYouTubeService
{
    private const int ThumbnailMinWidth = 240; // 2× the 120 px thumbnail box in MainWindow.axaml — sharp up to 200% DPI

    // YoutubeExplode 6.6.2 hard-codes a SOCS consent cookie that YouTube stopped accepting in the EU (Oct 2026):
    // every watch page then redirects to consent.youtube.com and fails with "Video watch page is broken".
    // A cookie passed here replaces the built-in one. Drop this once a YoutubeExplode release refreshes the value.
    private const string ConsentCookieValue = "CAISEwgDEgk2NzM5OTg2ODUaAmVuIAEaBgiA6p23Bg";

    private readonly YoutubeClient _client = new([new Cookie("SOCS", ConsentCookieValue) { Domain = "youtube.com" }]);

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct)
    {
        var video = await _client.Videos.GetAsync(url, ct);
        var manifest = await _client.Videos.Streams.GetManifestAsync(video.Id, ct);

        var audioOptions = manifest.GetAudioOnlyStreams()
            // IsAudioLanguageDefault is null when the manifest has no language data, false on a dubbed track.
            .Select(a => StreamMapping.ToAudioOption(a.AudioCodec, a.Bitrate.BitsPerSecond, a.Container.Name, a, a.IsAudioLanguageDefault))
            .ToList();

        if (audioOptions.Count == 0)
            throw new InvalidOperationException("This video has no downloadable audio stream.");

        var bestAudio = StreamSelector.PickBestAudio(audioOptions);
        var bestOpus = StreamSelector.PickBestOpusAudio(audioOptions);
        var bestMp4Audio = StreamSelector.PickBestMp4Audio(audioOptions);

        var videoOptions = StreamSelector.OrderVideoOptions(
            manifest.GetVideoOnlyStreams()
                .Select(v => StreamMapping.ToVideoOption(v.VideoQuality.Label, v.VideoQuality.MaxHeight, v.Container.Name, v)));

        var thumb = ThumbnailPicker.Pick(
            video.Thumbnails.Select(t => (t.Url, t.Resolution.Width, t.Resolution.Height)),
            ThumbnailMinWidth);

        return new VideoInfo(
            SourceUrl: url,
            Title: video.Title,
            Author: video.Author.ChannelTitle,
            Duration: video.Duration ?? TimeSpan.Zero,
            ThumbnailUrl: thumb,
            BestAudio: bestAudio,
            VideoOptions: videoOptions,
            BestOpusAudio: bestOpus,
            BestMp4Audio: bestMp4Audio,
            UploadDate: video.UploadDate,
            CoverArtUrl: video.Thumbnails.TryGetWithHighestResolution()?.Url);
    }

    public async Task DownloadStreamAsync(IStreamInfo stream, string filePath, IProgress<double>? progress, CancellationToken ct)
        => await _client.Videos.Streams.DownloadAsync(stream, filePath, progress, ct);

    public async Task<PlaylistInfo> GetPlaylistAsync(string listId, CancellationToken ct)
        => PlaylistMapping.ToPlaylistInfo(await _client.Playlists.GetAsync(listId, ct), ThumbnailMinWidth);

    public async IAsyncEnumerable<IReadOnlyList<QueueItem>> GetPlaylistItemBatchesAsync(string listId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var index = 0;
        await foreach (var batch in _client.Playlists.GetVideoBatchesAsync(listId, ct))
        {
            var items = new List<QueueItem>(batch.Items.Count);
            foreach (var video in batch.Items)
                items.Add(PlaylistMapping.ToQueueItem(video, ++index, ThumbnailMinWidth));
            if (items.Count > 0)
                yield return items;
        }
    }
}
