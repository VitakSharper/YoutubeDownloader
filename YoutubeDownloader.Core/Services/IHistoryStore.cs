using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IHistoryStore
{
    /// <summary>Loads saved entries (most-recent-first). Returns empty if none or unreadable.</summary>
    IReadOnlyList<HistoryEntry> Load();

    /// <summary>Persists the full entry list, overwriting the store.</summary>
    void Save(IEnumerable<HistoryEntry> entries);
}
