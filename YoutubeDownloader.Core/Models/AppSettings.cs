namespace YoutubeDownloader.Core.Models;

/// <summary>User preferences persisted across runs.</summary>
public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Folder of the most recent save, used to pre-select the save dialog location.</summary>
    public string LastSaveFolder { get; set; } = "";

    /// <summary>Last-used <see cref="DownloadMode"/> name, e.g. "AudioOpus". Empty = default (MP3).</summary>
    public string LastMode { get; set; } = "";

    /// <summary><see cref="Mp3Quality.Key"/> of the last-used MP3 preset. Empty = default (best VBR).</summary>
    public string LastMp3Quality { get; set; } = "";
}
