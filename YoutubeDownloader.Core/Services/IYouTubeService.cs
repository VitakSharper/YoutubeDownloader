using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IYouTubeService
{
    Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct);
    Task DownloadStreamAsync(IStreamInfo stream, string filePath, IProgress<double>? progress, CancellationToken ct);

    /// <summary>Title, author and reported count of a list. Throws PlaylistUnavailableException for private or missing lists.</summary>
    Task<PlaylistInfo> GetPlaylistAsync(string listId, CancellationToken ct);

    /// <summary>
    /// The list's items, one batch per page YouTube returns (about 200), with 1-based indexes continuing
    /// across batches. Lets the UI show "Loading… (400 so far)" without one update per item.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<QueueItem>> GetPlaylistItemBatchesAsync(string listId, CancellationToken ct);
}
