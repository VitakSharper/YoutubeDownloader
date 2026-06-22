using System.Text.Json;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

/// <summary>Persists download history as a JSON file (history.json) in the given directory.</summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;

    public JsonHistoryStore(string directory)
    {
        _filePath = Path.Combine(directory, "history.json");
    }

    public IReadOnlyList<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<HistoryEntry>();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, Options) ?? new List<HistoryEntry>();
        }
        catch
        {
            // Unreadable/corrupt history must never crash the app — start empty.
            return new List<HistoryEntry>();
        }
    }

    public void Save(IEnumerable<HistoryEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(entries.ToList(), Options);
        File.WriteAllText(_filePath, json);
    }
}
