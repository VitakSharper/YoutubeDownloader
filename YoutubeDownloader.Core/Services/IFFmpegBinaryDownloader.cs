namespace YoutubeDownloader.Core.Services;

public interface IFFmpegBinaryDownloader
{
    Task DownloadAsync(string targetDir, IProgress<double>? progress, CancellationToken ct);
}
