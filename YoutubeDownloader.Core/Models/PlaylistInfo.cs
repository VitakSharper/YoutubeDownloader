namespace YoutubeDownloader.Core.Models;

/// <param name="Author">Null for Mixes and YouTube Music albums.</param>
/// <param name="Count">What YouTube reports; null for Mixes and some large lists, and it can exceed the
/// number of items actually enumerated (private/deleted entries are hidden).</param>
/// <param name="ThumbnailUrl">For the playlist card, or null.</param>
public sealed record PlaylistInfo(string ListId, string Title, string? Author, int? Count, string? ThumbnailUrl);
