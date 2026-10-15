namespace YoutubeDownloader.Core.Models;

/// <summary>A date-labelled bucket of history entries for the grouped UI.</summary>
public sealed class HistoryGroup
{
    /// <param name="rows">What the History tab shows; by default one plain row per entry.</param>
    public HistoryGroup(string header, IReadOnlyList<HistoryEntry> entries, IReadOnlyList<HistoryRow>? rows = null)
    {
        Header = header;
        Entries = entries;
        Rows = rows ?? entries.Select(entry => (HistoryRow)new HistoryEntryRow(entry)).ToList();
    }

    public string Header { get; }

    /// <summary>Every entry of the group, flat, most-recent-first.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get; }

    /// <summary>The same entries as displayed: single cards, and one collapsible row per playlist run.</summary>
    public IReadOnlyList<HistoryRow> Rows { get; }
}
