namespace YoutubeDownloader.Core.Models;

/// <summary>What one download produces. Closed set: a switch over it ends with UnreachableException.</summary>
public abstract record OutputFormat
{
    private OutputFormat() { }

    public sealed record Mp3(Mp3Quality Quality) : OutputFormat;
    public sealed record Opus : OutputFormat;
    public sealed record Mp4(VideoStreamOption Video) : OutputFormat;
}
