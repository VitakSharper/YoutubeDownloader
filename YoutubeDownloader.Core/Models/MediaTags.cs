namespace YoutubeDownloader.Core.Models;

/// <summary>Text metadata written into downloaded audio files.</summary>
/// <param name="Album">Album title, or null for none.</param>
/// <param name="Track">1-based track number, or null.</param>
/// <param name="TrackCount">Tracks in the album; written as "Track/TrackCount" when both are set.</param>
public sealed record MediaTags(string Title, string Artist, string SourceUrl, int? Year,
    string? Album = null, int? Track = null, int? TrackCount = null);
