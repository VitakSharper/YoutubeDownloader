using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Services;

public sealed class XabeFFmpegBinaryDownloader : IFFmpegBinaryDownloader
{
    /// <remarks>
    /// Xabe 6.0.2 exposes no CancellationToken, so <paramref name="ct"/> is not observed here;
    /// FFmpegLocator lets callers stop waiting while the install finishes in the background.
    /// </remarks>
    public async Task DownloadAsync(string targetDir, IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0.0);
        var aggregator = new DownloadProgressAggregator();
        IProgress<ProgressInfo>? xabeProgress = progress is null
            ? null
            : new CallbackProgress<ProgressInfo>(p => progress.Report(0.95 * aggregator.Report(p.DownloadedBytes, p.TotalBytes)));

        // Task.Run: Xabe performs a synchronous WebClient request before its first await.
        await Task.Run(() => FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, targetDir, xabeProgress));
        progress?.Report(1.0);
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;
        public CallbackProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }
}
