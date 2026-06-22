using Xabe.FFmpeg.Downloader;

namespace YoutubeDownloader.Core.Services;

public sealed class XabeFFmpegBinaryDownloader : IFFmpegBinaryDownloader
{
    public async Task DownloadAsync(string targetDir, IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0.0);
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, targetDir);
        progress?.Report(1.0);
    }
}
