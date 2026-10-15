using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class HistoryRowsTests
{
    private const string RunId = "0123456789abcdef0123456789abcdef";

    private static HistoryEntry Single(string title) => new() { Url = "https://youtu.be/" + title, Title = title };

    private static HistoryEntry Track(int index, string runId = RunId) => new()
    {
        Url = $"https://www.youtube.com/watch?v=vid{index:D8}",
        Title = $"Track {index}",
        VideoId = $"vid{index:D8}",
        PlaylistId = "PL601B2E69B03FAB9D",
        PlaylistTitle = "'80s Sing-Alongs",
        RunId = runId,
        TrackIndex = index
    };

    private static HashSet<string> NoneExpanded() => new(StringComparer.Ordinal);

    [Fact]
    public void EntriesWithoutRun_StayPlainRows()
    {
        var entries = new[] { Single("a"), Single("b") };

        var rows = HistoryRows.Build(entries, NoneExpanded(), forceExpanded: false);

        Assert.Equal(entries, rows.Select(row => Assert.IsType<HistoryEntryRow>(row).Entry));
    }

    [Fact]
    public void RunEntries_BecomeOneRowWithTitleAndCount()
    {
        var newer = Single("newer");
        var older = Single("older");
        var entries = new[] { newer, Track(3), Track(2), Track(1), older };

        var rows = HistoryRows.Build(entries, NoneExpanded(), forceExpanded: false);

        Assert.Equal(3, rows.Count);
        Assert.Same(newer, Assert.IsType<HistoryEntryRow>(rows[0]).Entry);
        var run = Assert.IsType<HistoryRunRow>(rows[1]);
        Assert.Equal("'80s Sing-Alongs — 3 saved", run.Header);
        Assert.Equal(RunId, run.RunId);
        Assert.Equal(entries[1..4], run.Entries);
        Assert.False(run.IsExpanded);
        Assert.Same(older, Assert.IsType<HistoryEntryRow>(rows[2]).Entry);
    }

    [Fact]
    public void SingleEntryRun_StaysAPlainRow()
    {
        var retried = Track(5, runId: "fedcba9876543210fedcba9876543210");
        var entries = new[] { retried, Single("a") };

        var rows = HistoryRows.Build(entries, NoneExpanded(), forceExpanded: false);

        Assert.Equal(entries, rows.Select(row => Assert.IsType<HistoryEntryRow>(row).Entry));
    }

    [Fact]
    public void Expansion_IsRememberedAndForcedWhileSearching()
    {
        var entries = new[] { Track(2), Track(1) };
        var expanded = new HashSet<string>(StringComparer.Ordinal) { RunId };

        var remembered = Assert.IsType<HistoryRunRow>(Assert.Single(HistoryRows.Build(entries, expanded, forceExpanded: false)));
        Assert.True(remembered.IsExpanded);

        var forced = Assert.IsType<HistoryRunRow>(Assert.Single(HistoryRows.Build(entries, NoneExpanded(), forceExpanded: true)));
        Assert.True(forced.IsExpanded);

        remembered.IsExpanded = false;
        Assert.DoesNotContain(RunId, expanded);
        Assert.False(Assert.IsType<HistoryRunRow>(Assert.Single(HistoryRows.Build(entries, expanded, forceExpanded: false))).IsExpanded);

        remembered.IsExpanded = true;
        Assert.Contains(RunId, expanded);
    }
}
