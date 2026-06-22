namespace YoutubeDownloader.Core.Models;

/// <summary>User preferences persisted across runs.</summary>
public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; } = true;
}
