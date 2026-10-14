namespace YoutubeDownloader.Core.Models;

/// <summary>A save-dialog file-type filter: display label plus glob pattern (e.g. "Opus audio", "*.opus").</summary>
public sealed record SaveFileType(string Label, string Pattern);
