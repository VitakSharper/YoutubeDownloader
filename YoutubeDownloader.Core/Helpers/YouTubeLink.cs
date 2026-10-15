using YoutubeDownloader.Core.Models;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>What a pasted text points at. Pure and total: every input maps to exactly one case.</summary>
public abstract record YouTubeLink
{
    private const string ListParameter = "list";

    private static readonly string[] SchemelessPrefixes =
        ["youtube.com/", "www.youtube.com/", "m.youtube.com/", "music.youtube.com/", "youtu.be/"];

    private static readonly string[] YouTubeHosts =
        ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be"];

    private YouTubeLink() { }

    public sealed record Video(string VideoId) : YouTubeLink;
    public sealed record Playlist(string ListId, ListKind Kind) : YouTubeLink;
    public sealed record VideoInPlaylist(string VideoId, string ListId, ListKind Kind) : YouTubeLink;
    public sealed record Invalid : YouTubeLink;

    /// <summary>
    /// Classifies a pasted text. A list id counts only inside a YouTube URL: a bare text keeps today's
    /// single-video semantics, because YoutubeExplode reads almost any bare word as a playlist id.
    /// </summary>
    public static YouTubeLink Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new Invalid();

        var trimmed = text.Trim();
        var candidate = WithScheme(trimmed);
        return YouTubeUriOf(candidate) is { } uri
            ? Classify(VideoIdOf(uri, candidate), ListIdOf(uri))
            : FromBareText(trimmed);
    }

    private static string WithScheme(string trimmed) =>
        SchemelessPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ? "https://" + trimmed
            : trimmed;

    private static Uri? YouTubeUriOf(string candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && IsHttp(uri) && IsYouTubeHost(uri.Host)
            ? uri
            : null;

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsYouTubeHost(string host) =>
        YouTubeHosts.Contains(host.ToLowerInvariant(), StringComparer.Ordinal);

    private static YouTubeLink FromBareText(string trimmed) =>
        VideoId.TryParse(trimmed) is { } video ? new Video(video.Value) : new Invalid();

    private static YouTubeLink Classify(string? videoId, string? listId) => (videoId, listId) switch
    {
        ({ } video, { } list) => new VideoInPlaylist(video, list, KindOf(list)),
        (null, { } list) => new Playlist(list, KindOf(list)),
        ({ } video, null) => new Video(video),
        (null, null) => new Invalid()
    };

    /// <summary>The embed playlist URL (<c>/embed/videoseries?list=…</c>) would otherwise parse as the video id "videoseries".</summary>
    private static string? VideoIdOf(Uri uri, string candidate) =>
        uri.AbsolutePath.StartsWith("/embed/videoseries", StringComparison.OrdinalIgnoreCase)
            ? null
            : VideoId.TryParse(candidate)?.Value;

    /// <summary>Read from the query directly: <c>PlaylistId.TryParse</c> misses the share link <c>youtu.be/&lt;id&gt;?list=…</c>.</summary>
    private static string? ListIdOf(Uri uri) =>
        QueryValue(uri.Query, ListParameter) is { Length: > 0 } value && PlaylistId.TryParse(value) is not null
            ? value
            : null;

    private static string? QueryValue(string query, string key) =>
        query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            .Select(parts => Unescape(parts[1]))
            .FirstOrDefault();

    private static string? Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static ListKind KindOf(string listId) => listId switch
    {
        "WL" or "LL" or "LM" => ListKind.PrivateToAccount,
        _ when listId.StartsWith("RDCLAK5uy_", StringComparison.Ordinal) => ListKind.Playlist,
        _ when listId.StartsWith("RDMM", StringComparison.Ordinal) => ListKind.PrivateToAccount,
        _ when listId.StartsWith("RD", StringComparison.Ordinal) => ListKind.Mix,
        _ when listId.StartsWith("OLAK5uy_", StringComparison.Ordinal) => ListKind.Album,
        _ => ListKind.Playlist
    };
}
