namespace YoutubeDownloader.Core.Services;

public interface IFFmpegLocator
{
    Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken ct);
}
