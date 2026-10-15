using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Core.Tests;

public class PlaylistViewModelTests
{
    private const string ListId = "PL601B2E69B03FAB9D";

    private static QueueItem Item(int i) =>
        new($"vid{i:D8}", PlaylistMapping.WatchUrl($"vid{i:D8}"), $"Title {i}", "Channel", TimeSpan.FromMinutes(3), i, null);

    private static PlaylistViewModel WithItems(int count)
    {
        var playlist = new PlaylistViewModel(ListId, ListKind.Playlist);
        playlist.AddItems(Enumerable.Range(1, count).Select(i => Item(i)));
        return playlist;
    }

    [Fact]
    public void AddItems_SelectsEveryItemAndCountsThem()
    {
        var playlist = WithItems(3);

        Assert.All(playlist.Items, row => Assert.True(row.IsSelected));
        Assert.Equal(3, playlist.SelectedCount);
        Assert.Equal("Select all (3 of 3)", playlist.SelectAllText);
        Assert.True(playlist.AllSelected);
        Assert.Same(playlist.Items[1], playlist.Find("vid00000002"));
    }

    [Fact]
    public void AllSelectedFalse_UnticksEveryRow_ThenTrueTicksThemAgain()
    {
        var playlist = WithItems(3);
        var raised = new List<string?>();
        playlist.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        playlist.AllSelected = false;

        Assert.All(playlist.Items, row => Assert.False(row.IsSelected));
        Assert.Equal(0, playlist.SelectedCount);
        Assert.Equal("Select all (0 of 3)", playlist.SelectAllText);
        Assert.False(playlist.AllSelected);
        Assert.Single(raised, name => name == nameof(PlaylistViewModel.SelectAllText)); // one refresh for the whole bulk change

        playlist.AllSelected = true;

        Assert.All(playlist.Items, row => Assert.True(row.IsSelected));
        Assert.Equal("Select all (3 of 3)", playlist.SelectAllText);
        Assert.True(playlist.AllSelected);
    }

    [Fact]
    public void UntickingOneRow_UpdatesTheCountAndSelectAll()
    {
        var playlist = WithItems(3);
        var raised = new List<string?>();
        playlist.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        playlist.Items[1].IsSelected = false;

        Assert.Equal(2, playlist.SelectedCount);
        Assert.Equal("Select all (2 of 3)", playlist.SelectAllText);
        Assert.False(playlist.AllSelected);
        Assert.Contains(nameof(PlaylistViewModel.AllSelected), raised);
        Assert.Contains(nameof(PlaylistViewModel.SelectAllText), raised);

        playlist.Items[1].IsSelected = true;

        Assert.True(playlist.AllSelected);
    }

    [Fact]
    public void HiddenNote_OnlyAfterLoadingAndOnlyWhenCountExceedsItems()
    {
        var playlist = new PlaylistViewModel(ListId, ListKind.Playlist)
        {
            Info = new PlaylistInfo(ListId, "Long videos", "a user", 99, null)
        };
        playlist.AddItems(Enumerable.Range(1, 96).Select(i => Item(i)));

        Assert.Null(playlist.HiddenNote); // still loading: more items may come
        Assert.Equal("a user · Loading… (96 so far)", playlist.Subtitle);

        playlist.FinishLoading();

        Assert.Equal("3 unavailable videos are hidden by YouTube", playlist.HiddenNote);
        Assert.Equal("a user · 96 videos", playlist.Subtitle);

        var complete = new PlaylistViewModel(ListId, ListKind.Playlist) { Info = new PlaylistInfo(ListId, "Pharmacology", "Armando Hasudungan", 3, null) };
        complete.AddItems(Enumerable.Range(1, 3).Select(i => Item(i)));
        complete.FinishLoading();
        Assert.Null(complete.HiddenNote);

        var noCount = new PlaylistViewModel(ListId, ListKind.Playlist) { Info = new PlaylistInfo(ListId, "Large", "a user", null, null) };
        noCount.AddItems(Enumerable.Range(1, 3).Select(i => Item(i)));
        noCount.FinishLoading();
        Assert.Null(noCount.HiddenNote);
    }

    [Theory]
    [InlineData(null, "24 h +")]
    [InlineData(568, "9:28")]
    [InlineData(3723, "1:02:03")]
    public void DurationText_FormatsOrSaysTwentyFourHoursPlus(int? seconds, string expected)
    {
        var item = Item(1) with { Duration = seconds is { } s ? TimeSpan.FromSeconds(s) : null };

        var row = new QueueItemViewModel(item);

        Assert.Equal(expected, row.DurationText);
        Assert.Equal("001", row.NumberText);
    }
}
