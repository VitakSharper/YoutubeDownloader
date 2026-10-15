using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IDownloadPipeline
{
    /// <summary>
    /// Downloads, converts and commits one file. Never throws for an expected result: cancellation becomes
    /// <see cref="DownloadOutcome.Cancelled"/>, any other error <see cref="DownloadOutcome.Failed"/>.
    /// Always discards the ".part" file when nothing was committed and deletes its temp files.
    /// </summary>
    Task<DownloadOutcome> RunAsync(DownloadRequest request, IProgress<PipelineProgress> progress, CancellationToken ct);
}
