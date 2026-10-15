namespace YoutubeDownloader.Core.Models;

/// <summary>One item of a queue run.</summary>
/// <param name="TargetPath">Planned up front by OutputFileNamer (plan 023).</param>
public sealed record QueueJobItem(QueueItem Item, string TargetPath);

/// <summary>What one queue run downloads, and how.</summary>
/// <param name="Items">Only the items to run, in order (unticked and already-saved ones are left out by the caller).</param>
/// <param name="FfmpegPath">Ensured once by the caller before the run.</param>
public sealed record QueueJob(IReadOnlyList<QueueJobItem> Items, QueueFormat Format, string FfmpegPath);
