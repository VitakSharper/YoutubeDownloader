namespace YoutubeDownloader.Core.Models;

/// <summary>What a queue run reports while it runs. Closed set: a switch over it ends with UnreachableException.</summary>
public abstract record QueueEvent
{
    private QueueEvent() { }

    public sealed record ItemChanged(QueueItem Item, ItemState State) : QueueEvent;

    /// <summary>YouTube answered 429: the whole queue waits this long before trying again.</summary>
    public sealed record RateLimitPause(TimeSpan Wait) : QueueEvent;
}
