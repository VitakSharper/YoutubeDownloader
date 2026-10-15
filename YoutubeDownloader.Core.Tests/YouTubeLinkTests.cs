using System.Diagnostics;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class YouTubeLinkTests
{
    private const string L = "PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e";
    private const string A = "OLAK5uy_lLeonUugocG5J0EUAEDmbskX4emejKwcM";

    [Theory]
    // The spike's parse table (plan 018 §1).
    [InlineData("dQw4w9WgXcQ", "Video", "dQw4w9WgXcQ", null, null)]
    [InlineData("hello", "Invalid", null, null, null)]
    [InlineData(L, "Invalid", null, null, null)]
    [InlineData("https://www.youtube.com/watch?v=uPZSSdkGQhM&list=" + L + "&index=1", "VideoInPlaylist", "uPZSSdkGQhM", L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/watch?list=" + L + "&v=uPZSSdkGQhM", "VideoInPlaylist", "uPZSSdkGQhM", L, ListKind.Playlist)]
    [InlineData("https://youtu.be/uPZSSdkGQhM?list=" + L, "VideoInPlaylist", "uPZSSdkGQhM", L, ListKind.Playlist)]
    [InlineData("https://youtu.be/uPZSSdkGQhM?si=AbCdEf&list=" + L, "VideoInPlaylist", "uPZSSdkGQhM", L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/playlist?list=" + L, "Playlist", null, L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/playlist?list=" + L + "&si=XyZ", "Playlist", null, L, ListKind.Playlist)]
    [InlineData("https://music.youtube.com/playlist?list=" + A, "Playlist", null, A, ListKind.Album)]
    [InlineData("https://m.youtube.com/watch?v=uPZSSdkGQhM&list=" + L, "VideoInPlaylist", "uPZSSdkGQhM", L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/watch?v=TsYhxMnGYCw&list=RDTsYhxMnGYCw&start_radio=1", "VideoInPlaylist", "TsYhxMnGYCw", "RDTsYhxMnGYCw", ListKind.Mix)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=WL", "VideoInPlaylist", "dQw4w9WgXcQ", "WL", ListKind.PrivateToAccount)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=LL", "VideoInPlaylist", "dQw4w9WgXcQ", "LL", ListKind.PrivateToAccount)]
    [InlineData("https://www.youtube.com/embed/videoseries?list=" + L, "Playlist", null, L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "Video", "dQw4w9WgXcQ", null, null)]
    // Extra cases.
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=LM", "VideoInPlaylist", "dQw4w9WgXcQ", "LM", ListKind.PrivateToAccount)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=RDMMdQw4w9WgXcQ", "VideoInPlaylist", "dQw4w9WgXcQ", "RDMMdQw4w9WgXcQ", ListKind.PrivateToAccount)]
    [InlineData("https://music.youtube.com/playlist?list=RDCLAK5uy_lf8okgl2ygD075nhnJVjlfhwp8NsUgEbs", "Playlist", null, "RDCLAK5uy_lf8okgl2ygD075nhnJVjlfhwp8NsUgEbs", ListKind.Playlist)]
    [InlineData("https://www.youtube.com/playlist?list=UUTMt7iMWa7jy0fNXIktwyLA", "Playlist", null, "UUTMt7iMWa7jy0fNXIktwyLA", ListKind.Playlist)]
    [InlineData("https://music.youtube.com/watch?v=uPZSSdkGQhM&list=" + A, "VideoInPlaylist", "uPZSSdkGQhM", A, ListKind.Album)]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ&list=" + L, "Invalid", null, null, null)]
    [InlineData("youtube.com/playlist?list=" + L, "Playlist", null, L, ListKind.Playlist)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "Video", "dQw4w9WgXcQ", null, null)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=", "Video", "dQw4w9WgXcQ", null, null)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s&pp=ygUE", "Video", "dQw4w9WgXcQ", null, null)]
    [InlineData(null, "Invalid", null, null, null)]
    [InlineData("   ", "Invalid", null, null, null)]
    public void Parse_ClassifiesInput(string? input, string expectedCase, string? videoId, string? listId, ListKind? kind)
    {
        var expected = Expected(expectedCase, videoId, listId, kind);

        Assert.Equal(expected, YouTubeLink.Parse(input));
    }

    [Theory]
    [InlineData("https://")]
    [InlineData("http://youtube.com")]
    [InlineData("%%%")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=%ZZ")]
    public void Parse_NeverThrows(string input)
    {
        Assert.NotNull(YouTubeLink.Parse(input));
    }

    private static YouTubeLink Expected(string expectedCase, string? videoId, string? listId, ListKind? kind) => expectedCase switch
    {
        "Video" => new YouTubeLink.Video(videoId!),
        "Playlist" => new YouTubeLink.Playlist(listId!, kind!.Value),
        "VideoInPlaylist" => new YouTubeLink.VideoInPlaylist(videoId!, listId!, kind!.Value),
        "Invalid" => new YouTubeLink.Invalid(),
        _ => throw new UnreachableException($"Unknown expected case '{expectedCase}'.")
    };
}
