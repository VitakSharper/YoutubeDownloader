namespace YoutubeDownloader.Core.Models;

/// <summary>Result of one pipeline run. Expected results are values, never exceptions.</summary>
public abstract record DownloadOutcome
{
    private DownloadOutcome() { }

    public sealed record Saved(string Path, string FormatLabel) : DownloadOutcome;

    /// <param name="Cause">The exception behind the failure, when there is one; the queue (plan 025b) reads it
    /// to tell a full disk or a 403 from an FFmpeg error.</param>
    public sealed record Failed(string Message, Exception? Cause = null) : DownloadOutcome;

    public sealed record Cancelled : DownloadOutcome;
}
