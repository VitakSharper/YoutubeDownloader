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

    // Playlist-run fields (018 §8): optional, and never written when null, so a single download's JSON is
    // exactly what older versions wrote and read, and an older version reading a newer file ignores them.

    /// <summary>YouTube video id; null for entries written before playlist runs existed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VideoId { get; set; }

    /// <summary>Set on entries saved by a playlist run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaylistId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaylistTitle { get; set; }

    /// <summary>Same value for every entry of one run; groups them in the History tab.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunId { get; set; }

    /// <summary>The item's position in the playlist.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TrackIndex { get; set; }

    /// <summary>Local time-of-day for the UI (date is shown in the group header). Not persisted.</summary>
    [JsonIgnore]
    public string TimeDisplay => DownloadedAt.LocalDateTime.ToString("t", CultureInfo.CurrentCulture);
}
