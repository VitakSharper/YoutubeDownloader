namespace YoutubeDownloader.Core.Models;

/// <summary>Where one queue item stands. Every transition replaces the whole value.</summary>
public abstract record ItemState
{
    private ItemState() { }

    public sealed record Pending : ItemState;
    public sealed record Fetching(int Attempt) : ItemState;
    /// <param name="Progress">The pipeline fraction, 0..1 (plan 021).</param>
    public sealed record Downloading(PipelineStage Stage, double Progress) : ItemState;
    /// <param name="FormatLabel">The format as history records it, e.g. "MP3 · 320 kbps".</param>
    public sealed record Saved(string Path, string FormatLabel) : ItemState;
    public sealed record Failed(string Reason, FailureKind Kind) : ItemState;
    /// <summary>"Already saved" or "Removed".</summary>
    public sealed record Skipped(string Reason) : ItemState;
    public sealed record Cancelled : ItemState;

    /// <summary>Saved, Failed, Skipped or Cancelled: the item will not change again in this run.</summary>
    public bool IsFinal => this is Saved or Failed or Skipped or Cancelled;
}
