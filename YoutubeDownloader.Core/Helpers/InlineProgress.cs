namespace YoutubeDownloader.Core.Helpers;

/// <summary>
/// An IProgress that runs its handler synchronously on the reporting thread. Unlike Progress&lt;T&gt; it never
/// posts, so a report made before an await is applied before that await (see plan 021, "Threading rule").
/// </summary>
public sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
