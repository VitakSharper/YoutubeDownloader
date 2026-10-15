using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.ViewModels;

/// <summary>The playlist card and its checklist. Holds no services: the main view model feeds it.</summary>
public sealed partial class PlaylistViewModel : ObservableObject
{
    /// <summary>Maintainer answer 7: warn above 200 items, no hard cap.</summary>
    public const int LargeListThreshold = 200;

    private readonly Dictionary<string, QueueItemViewModel> _byVideoId = new(StringComparer.Ordinal);
    private int _selectedCount;
    private bool _bulkSelecting;

    public PlaylistViewModel(string listId, ListKind kind)
    {
        ListId = listId;
        Kind = kind;
        TagAsAlbum = kind == ListKind.Album;
    }

    public string ListId { get; }
    public ListKind Kind { get; }
    public ObservableCollection<QueueItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(ThumbnailUrl))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(HiddenNote))]
    private PlaylistInfo? _info;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(HiddenNote))]
    private bool _isLoading = true;

    public string Title => Info?.Title ?? "Playlist";
    public string? ThumbnailUrl => Info?.ThumbnailUrl;

    /// <summary>"Armando Hasudungan · 50 videos" (no author for Mixes and albums); "Loading… (400 so far)" while paging.</summary>
    public string Subtitle => string.Join(" · ", new[] { Info?.Author, CountText() }.Where(part => !string.IsNullOrEmpty(part)));

    public int SelectedCount => _selectedCount;

    /// <summary>Ticked rows not saved yet: what "Download N" will run (Pending, Failed, Skipped or Cancelled).</summary>
    public int RunnableCount => Items.Count(r => r.IsSelected && r.State is not ItemState.Saved);
    public string SelectAllText => $"Select all ({_selectedCount} of {Items.Count})";

    /// <summary>Two-way bound to the "Select all" checkbox.</summary>
    public bool AllSelected
    {
        get => Items.Count > 0 && _selectedCount == Items.Count;
        set => SelectAll(value);
    }

    /// <summary>"3 unavailable videos are hidden by YouTube" once loading ends and Count exceeds the items found (spike: 99 vs 96).</summary>
    public string? HiddenNote => !IsLoading && Info?.Count is { } count && count > Items.Count
        ? count - Items.Count == 1
            ? "1 unavailable video is hidden by YouTube"
            : $"{count - Items.Count} unavailable videos are hidden by YouTube"
        : null;

    /// <summary>Set once a download run starts on this card: the long-list warning has done its job.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LargeListWarning))]
    private bool _hasStartedRun;

    /// <summary>Shown until the first run starts, which then gets the warning's room back for its rows.</summary>
    public string? LargeListWarning => Items.Count > LargeListThreshold && !HasStartedRun
        ? $"{Items.Count} videos: a list this long can take hours to download."
        : null;

    /// <summary>
    /// "Tag as an album": the playlist title as the album, the position as the track number. Maintainer answer 8:
    /// on for YouTube Music albums (OLAK5uy_…), off for other lists. Not persisted.
    /// </summary>
    [ObservableProperty]
    private bool _tagAsAlbum;

    /// <summary>Adds one page of items, each ticked. A video listed twice keeps its first row for <see cref="Find"/>.</summary>
    public void AddItems(IEnumerable<QueueItem> batch)
    {
        foreach (var item in batch)
        {
            var row = new QueueItemViewModel(item);
            row.PropertyChanged += OnRowChanged;
            _byVideoId.TryAdd(item.VideoId, row);
            Items.Add(row);
            _selectedCount++;
        }
        RefreshCounts();
    }

    public void FinishLoading() => IsLoading = false;

    public QueueItemViewModel? Find(string videoId) => _byVideoId.GetValueOrDefault(videoId);

    /// <summary>"N videos" with the singular for one.</summary>
    public static string VideoCount(int count) => count == 1 ? "1 video" : $"{count} videos";

    private string CountText()
    {
        if (!IsLoading)
            return VideoCount(Items.Count);
        return Items.Count == 0 ? "Loading…" : $"Loading… ({Items.Count} so far)";
    }

    private void SelectAll(bool value)
    {
        _bulkSelecting = true;
        try
        {
            foreach (var row in Items)
                row.IsSelected = value;
        }
        finally
        {
            _bulkSelecting = false;
        }
        RefreshCounts();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QueueItemViewModel.IsSelected) || sender is not QueueItemViewModel row)
            return;
        _selectedCount += row.IsSelected ? 1 : -1;
        if (!_bulkSelecting)
            RefreshCounts();
    }

    /// <summary>Raises every count the card shows; the queue calls it when a run ends (row states changed).</summary>
    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(RunnableCount));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectAllText));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HiddenNote));
        OnPropertyChanged(nameof(LargeListWarning));
    }
}
