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

    /// <summary>Absolute path of the saved file (empty for entries created before this was tracked).</summary>
    public string FilePath { get; set; } = "";

    public DateTimeOffset DownloadedAt { get; set; }

    /// <summary>Local time-of-day for the UI (date is shown in the group header). Not persisted.</summary>
    [JsonIgnore]
    public string TimeDisplay => DownloadedAt.LocalDateTime.ToString("t", CultureInfo.CurrentCulture);
}
