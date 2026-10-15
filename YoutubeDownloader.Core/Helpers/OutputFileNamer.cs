using System.Globalization;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>One file to name: its track number (the playlist index), video title and channel name.</summary>
public sealed record NamingEntry(int Number, string Title, string Channel);

/// <summary>Plans queue file names: "NNN. Artist - Title.ext" (maintainer answer 2, 2026-10-15). Pure.</summary>
public static class OutputFileNamer
{
    /// <summary>Longest "Artist - Title" part kept in a file name (number, suffix and extension come on top).</summary>
    public const int MaxTrackNameLength = 150;

    private const string ArtistTitleSeparator = " - ";
    private const string TopicSuffix = " - Topic";
    private const string VevoSuffix = "VEVO";

    /// <summary>
    /// "Artist - Title". A video title that contains " - " already reads "Artist - Title" and is used as is.
    /// Otherwise Artist is the channel without a trailing " - Topic" or "VEVO", and Title the video title.
    /// </summary>
    public static string TrackName(string title, string channel)
    {
        if (title.Contains(ArtistTitleSeparator, StringComparison.Ordinal))
            return title;

        var artist = ArtistFromChannel(channel);
        return string.IsNullOrWhiteSpace(artist) ? title : $"{artist}{ArtistTitleSeparator}{title}";
    }

    /// <summary>The channel as an artist name: a trailing " - Topic" or "VEVO" removed, when something is left.</summary>
    public static string ArtistFromChannel(string channel)
    {
        var name = channel.Trim();
        if (name.EndsWith(TopicSuffix, StringComparison.Ordinal))
            return name[..^TopicSuffix.Length].Trim();

        if (name.EndsWith(VevoSuffix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(name[..^VevoSuffix.Length]))
            return name[..^VevoSuffix.Length].Trim();

        return name;
    }

    /// <summary>Digits for the track numbers: at least 3, more only past 999 (maintainer answer 2).</summary>
    public static int NumberWidth(int highestNumber) => Math.Max(3, highestNumber.ToString(CultureInfo.InvariantCulture).Length);

    /// <summary>
    /// One absolute target path per entry, in order: "{folder}\{NNN}. {TrackName}{suffix}.{extension}".
    /// A name already planned in this call, an existing file or its ".part" sibling (PartFilePath.For)
    /// gets " (2)", " (3)", … Existence is asked through <paramref name="fileExists"/>.
    /// </summary>
    public static IReadOnlyList<string> Plan(IReadOnlyList<NamingEntry> entries, string folder, string extension, Func<string, bool> fileExists)
    {
        if (entries.Count == 0)
            return [];

        var width = NumberWidth(entries.Max(e => e.Number));
        var ext = extension.TrimStart('.');
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            var path = FreePath(Stem(entry, width), folder, ext, planned, fileExists);
            planned.Add(path);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>
    /// Video ids that history says were already saved into <paramref name="folder"/> with this extension
    /// and whose file still exists there (maintainer answer 9: such items are skipped, not saved again).
    /// </summary>
    public static IReadOnlySet<string> AlreadySavedVideoIds(IEnumerable<HistoryEntry> history, string folder, string extension, Func<string, bool> fileExists)
    {
        var targetFolder = Path.TrimEndingDirectorySeparator(folder);
        var dotExtension = "." + extension.TrimStart('.');
        var ids = history
            .Select(entry => SavedVideoId(entry, targetFolder, dotExtension, fileExists))
            .OfType<string>();
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    /// <summary>The playlist's own sub-folder: "{parent}\{sanitized playlist title}" (maintainer answer 3).</summary>
    public static string SubFolder(string parent, string playlistTitle)
        => Path.Combine(parent, FileNameSanitizer.Sanitize(playlistTitle, MaxTrackNameLength));

    private static string Stem(NamingEntry entry, int width)
    {
        var number = entry.Number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        var trackName = FileNameSanitizer.Sanitize(TrackName(entry.Title, entry.Channel), MaxTrackNameLength);
        return $"{number}. {trackName}";
    }

    private static string FreePath(string stem, string folder, string ext, IReadOnlySet<string> planned, Func<string, bool> fileExists)
    {
        var candidate = Path.Combine(folder, $"{stem}.{ext}");
        for (var n = 2; IsTaken(candidate, planned, fileExists); n++)
            candidate = Path.Combine(folder, $"{stem} ({n}).{ext}");

        return candidate;
    }

    private static bool IsTaken(string candidate, IReadOnlySet<string> planned, Func<string, bool> fileExists)
        => planned.Contains(candidate) || fileExists(candidate) || fileExists(PartFilePath.For(candidate));

    /// <summary>The entry's video id when it was saved into <paramref name="folder"/> with this extension and the file is still there.</summary>
    private static string? SavedVideoId(HistoryEntry entry, string folder, string dotExtension, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(entry.FilePath))
            return null;

        var videoId = YouTubeUrlValidator.GetVideoId(entry.Url);
        var savedFolder = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(entry.FilePath) ?? "");
        var inFolder = string.Equals(savedFolder, folder, StringComparison.OrdinalIgnoreCase);
        var sameExtension = string.Equals(Path.GetExtension(entry.FilePath), dotExtension, StringComparison.OrdinalIgnoreCase);
        return videoId is not null && inFolder && sameExtension && fileExists(entry.FilePath) ? videoId : null;
    }
}
