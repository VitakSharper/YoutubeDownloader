namespace YoutubeDownloader.Core.Services;

/// <summary>The production <see cref="IRetryDelay"/>: a real wait on the given clock.</summary>
public sealed class TimeProviderRetryDelay : IRetryDelay
{
    private readonly TimeProvider _time;

    public TimeProviderRetryDelay(TimeProvider time) => _time = time;

    public Task WaitAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, _time, ct);
}
