namespace YoutubeDownloader.Core.Models;

/// <summary>
/// The format chosen once for a whole queue; QueueFormatResolver turns it into each item's OutputFormat.
/// Closed set: a switch over it ends with UnreachableException.
/// </summary>
public abstract record QueueFormat
{
    private QueueFormat() { }

    public sealed record Mp3(Mp3Quality Quality) : QueueFormat;
    public sealed record Opus : QueueFormat;
    /// <summary>Each item takes its tallest MP4 option not above <paramref name="MaxHeight"/>.</summary>
    public sealed record Mp4(int MaxHeight) : QueueFormat;
}
