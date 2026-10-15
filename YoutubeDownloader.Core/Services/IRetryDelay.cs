namespace YoutubeDownloader.Core.Services;

/// <summary>Waits before a retry. Injected so tests see the requested delays without waiting.</summary>
public interface IRetryDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken ct);
}
