using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IYouTubeService
{
    Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct);
    Task DownloadStreamAsync(IStreamInfo stream, string filePath, IProgress<double>? progress, CancellationToken ct);
}
