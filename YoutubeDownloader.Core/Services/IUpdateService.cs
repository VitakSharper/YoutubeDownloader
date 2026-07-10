using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IUpdateService
{
    /// <summary>Queries the latest release; never throws for "no release" (returns LatestVersion == null).</summary>
    Task<UpdateInfo> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads + extracts the update zip; returns the extracted .exe path.</summary>
    Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default);
}
