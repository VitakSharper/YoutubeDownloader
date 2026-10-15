namespace YoutubeDownloader.Core.Models;

/// <summary>One item of a queue run.</summary>
/// <param name="TargetPath">Planned up front by OutputFileNamer (plan 023).</param>
public sealed record QueueJobItem(QueueItem Item, string TargetPath);

/// <summary>A run tagged as one album (plan 027a): the album title and the number of tracks listed.</summary>
public sealed record AlbumTagging(string Title, int TrackCount);

/// <summary>What one queue run downloads, and how.</summary>
/// <param name="Items">Only the items to run, in order (unticked and already-saved ones are left out by the caller).</param>
/// <param name="FfmpegPath">Ensured once by the caller before the run.</param>
/// <param name="Album">Album/track tags for every item, or null to write none.</param>
public sealed record QueueJob(IReadOnlyList<QueueJobItem> Items, QueueFormat Format, string FfmpegPath, AlbumTagging? Album = null);
