namespace YoutubeDownloader.Core.Models;

/// <summary>User preferences persisted across runs.</summary>
public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Folder of the most recent save, used to pre-select the save dialog location.</summary>
    public string LastSaveFolder { get; set; } = "";
}
