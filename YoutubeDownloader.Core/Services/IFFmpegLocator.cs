namespace YoutubeDownloader.Core.Services;

public interface IFFmpegLocator
{
    /// <summary>True when ffmpeg.exe is already installed, i.e. EnsureAsync will not download.</summary>
    bool IsAvailable { get; }

    Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken ct);
}
