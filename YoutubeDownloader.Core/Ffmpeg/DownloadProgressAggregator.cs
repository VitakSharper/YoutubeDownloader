namespace YoutubeDownloader.Core.Ffmpeg;

/// <summary>
/// Combines byte-level progress from several concurrent file downloads that share one progress sink
/// (Xabe reports ffmpeg and ffprobe into the same IProgress) into one monotonic 0..1 fraction.
/// Files are told apart by their total size. Thread-safe.
/// </summary>
public sealed class DownloadProgressAggregator
{
    private readonly object _gate = new();
    private readonly Dictionary<long, long> _downloadedByTotal = new();
    private double _last;

    /// <summary>Record one report and return the overall fraction (never lower than a previous result).</summary>
    public double Report(long downloadedBytes, long totalBytes)
    {
        lock (_gate)
        {
            if (totalBytes > 1) // totalBytes <= 1 is Xabe's "file finished" sentinel (1, 1) or unknown: ignore
                _downloadedByTotal[totalBytes] = Math.Clamp(downloadedBytes, 0, totalBytes);

            var total = _downloadedByTotal.Keys.Sum();
            var done = _downloadedByTotal.Values.Sum();
            var fraction = total > 0 ? (double)done / total : 0.0;
            _last = Math.Max(_last, fraction);
            return _last;
        }
    }
}
