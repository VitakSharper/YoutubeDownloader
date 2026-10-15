using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>Turns one date group's entries into the History tab's rows: single cards and playlist-run sub-headers (018 §8).</summary>
public static class HistoryRows
{
    /// <summary>
    /// Rows for one date group, in the entries' order. Entries sharing a RunId become one run row placed where
    /// the run's first (newest) entry is; a run of a single entry stays a plain row; entries without a RunId stay plain.
    /// </summary>
    public static IReadOnlyList<HistoryRow> Build(IReadOnlyList<HistoryEntry> entries, ISet<string> expandedRuns, bool forceExpanded)
    {
        var runs = entries
            .Where(entry => entry.RunId is not null)
            .GroupBy(entry => entry.RunId!, StringComparer.Ordinal)
            .Where(run => run.Skip(1).Any())
            .ToDictionary(run => run.Key, run => (IReadOnlyList<HistoryEntry>)run.ToList(), StringComparer.Ordinal);

        var placed = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<HistoryRow>();
        foreach (var entry in entries)
        {
            if (entry.RunId is { } runId && runs.TryGetValue(runId, out var run))
            {
                if (placed.Add(runId))
                    rows.Add(new HistoryRunRow(runId, Header(run), run, expandedRuns, forceExpanded));
            }
            else
            {
                rows.Add(new HistoryEntryRow(entry));
            }
        }
        return rows;
    }

    /// <summary>"'80s Sing-Alongs — 84 saved": history holds saved entries only, so the run's full size is not known here.</summary>
    private static string Header(IReadOnlyList<HistoryEntry> run)
        => $"{run[0].PlaylistTitle ?? "Playlist"} — {run.Count} saved";
}
