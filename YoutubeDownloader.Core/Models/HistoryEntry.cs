using System.Globalization;
using System.Text.Json.Serialization;

namespace YoutubeDownloader.Core.Models;

/// <summary>One completed download, persisted to the history file.</summary>
public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Display string for what was produced, e.g. "MP3 · 192 kbps" or "MP4 · 720p".</summary>
    public string Format { get; set; } = "";

    public DateTimeOffset DownloadedAt { get; set; }

    /// <summary>Local, human-friendly timestamp for the UI (not persisted).</summary>
    [JsonIgnore]
    public string WhenDisplay => DownloadedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
}
