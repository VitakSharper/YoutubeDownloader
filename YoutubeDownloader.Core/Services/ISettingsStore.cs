using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface ISettingsStore
{
    /// <summary>Loads saved settings, or defaults if none/unreadable.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
