namespace YoutubeDownloader.Core.Models;

/// <summary>A date-labelled bucket of history entries for the grouped UI.</summary>
public sealed class HistoryGroup
{
    public HistoryGroup(string header, IReadOnlyList<HistoryEntry> entries)
    {
        Header = header;
        Entries = entries;
    }

    public string Header { get; }
    public IReadOnlyList<HistoryEntry> Entries { get; }
}
