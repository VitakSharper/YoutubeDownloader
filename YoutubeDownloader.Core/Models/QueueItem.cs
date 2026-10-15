namespace YoutubeDownloader.Core.Models;

/// <summary>One playlist entry, as enumerated. Immutable.</summary>
/// <param name="Url">https://www.youtube.com/watch?v=&lt;id&gt; — never the URL YoutubeExplode gives a playlist entry, which carries &amp;list=.</param>
/// <param name="Author">The channel title.</param>
/// <param name="Duration">For display only: null for videos of 24 h or more (spike).</param>
/// <param name="Index">1-based, in enumeration order (YoutubeExplode exposes no playlist index).</param>
public sealed record QueueItem(
    string VideoId,
    string Url,
    string Title,
    string Author,
    TimeSpan? Duration,
    int Index,
    string? ThumbnailUrl);
