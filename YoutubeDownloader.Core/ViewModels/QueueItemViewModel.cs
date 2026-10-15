using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.ViewModels;

/// <summary>One checklist row: the immutable item plus what the UI changes (ticked, state).</summary>
public sealed partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(QueueItem item) => Item = item;

    public QueueItem Item { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    private ItemState _state = new ItemState.Pending();

    /// <summary>"001": the same number the file name gets (plan 023).</summary>
    public string NumberText => Item.Index.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>"9:28", "1:02:03"; "24 h +" when YouTube gives no duration (018 §9: those were all ≥ 24 h).</summary>
    public string DurationText => Item.Duration is { } d
        ? d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d:mm\\:ss}" : d.ToString(@"m\:ss", CultureInfo.InvariantCulture)
        : "24 h +";

    public string StatusText => ItemStateText.For(State);
    public double Progress => State is ItemState.Downloading downloading ? downloading.Progress : 0;
    public bool IsDownloading => State is ItemState.Downloading;
    public bool IsFailed => State is ItemState.Failed;
}
