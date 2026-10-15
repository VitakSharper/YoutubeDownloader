using System.Diagnostics;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

public static class ItemStateText
{
    /// <summary>The status column of a checklist row. Pending shows nothing (executor default).</summary>
    public static string For(ItemState state) => state switch
    {
        ItemState.Pending => "",
        ItemState.Fetching { Attempt: 1 } => "Fetching…",
        ItemState.Fetching fetching => $"Fetching… (retry {fetching.Attempt - 1})",
        ItemState.Downloading downloading => downloading.Stage switch
        {
            PipelineStage.DownloadingAudio or PipelineStage.DownloadingVideo => "Downloading…",
            PipelineStage.Converting => "Converting…",
            PipelineStage.SavingOpus => "Saving…",
            PipelineStage.Merging => "Merging…",
            _ => throw new UnreachableException()
        },
        ItemState.Saved => "✓ Saved",
        ItemState.Failed failed => failed.Reason,
        ItemState.Skipped skipped => skipped.Reason,
        ItemState.Cancelled => "Cancelled",
        _ => throw new UnreachableException()
    };
}
