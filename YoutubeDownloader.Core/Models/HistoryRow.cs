using System.ComponentModel;

namespace YoutubeDownloader.Core.Models;

/// <summary>One line inside a date group: a single entry, or one playlist run. Closed to this assembly.</summary>
public abstract class HistoryRow
{
    private protected HistoryRow() { }
}

/// <summary>A single entry, shown as its card.</summary>
public sealed class HistoryEntryRow : HistoryRow
{
    public HistoryEntryRow(HistoryEntry entry) => Entry = entry;

    public HistoryEntry Entry { get; }
}

/// <summary>A playlist run's entries under one collapsible sub-header ("'80s Sing-Alongs — 84 saved").</summary>
public sealed class HistoryRunRow : HistoryRow, INotifyPropertyChanged
{
    private readonly ISet<string> _expandedRuns;
    private bool _isExpanded;

    /// <param name="expandedRuns">The view model's set of runs the user expanded; this row reads and updates it.</param>
    /// <param name="forceExpanded">Expanded whatever the set says (a search is active), without recording it in the set.</param>
    public HistoryRunRow(string runId, string header, IReadOnlyList<HistoryEntry> entries, ISet<string> expandedRuns, bool forceExpanded)
    {
        RunId = runId;
        Header = header;
        Entries = entries;
        _expandedRuns = expandedRuns;
        _isExpanded = forceExpanded || expandedRuns.Contains(runId);
    }

    public string RunId { get; }
    public string Header { get; }
    public IReadOnlyList<HistoryEntry> Entries { get; }

    /// <summary>Two-way bound to the Expander; remembered in the view model's set so a rebuild keeps it.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            // Unchanged values are ignored, so a forced expansion echoed back never becomes a remembered one.
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            if (value)
                _expandedRuns.Add(RunId);
            else
                _expandedRuns.Remove(RunId);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
