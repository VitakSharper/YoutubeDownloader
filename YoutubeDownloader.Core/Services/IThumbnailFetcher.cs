namespace YoutubeDownloader.Core.Services;

/// <summary>Downloads a thumbnail image to a temp file for use as cover art.</summary>
public interface IThumbnailFetcher
{
    /// <summary>Returns the temp file path, or null on any failure (cover art is best-effort). Cancellation is rethrown.</summary>
    Task<string?> TryDownloadAsync(string? url, CancellationToken ct);
}
