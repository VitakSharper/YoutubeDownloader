namespace YoutubeDownloader.Core.Models;

/// <summary>Text metadata written into downloaded audio files.</summary>
public sealed record MediaTags(string Title, string Artist, string SourceUrl, int? Year);
