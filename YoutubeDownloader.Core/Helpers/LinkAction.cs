using System.Diagnostics;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>What the Download tab does with a classified link in v1: Mix and account-private lists stay video-only.</summary>
public abstract record LinkAction
{
    private LinkAction() { }

    public const string InvalidMessage = "That doesn't look like a valid YouTube URL.";
    public const string PrivateListHint = "That playlist is private to your YouTube account — downloading just the video.";
    public const string MixHint = "YouTube Mixes change on every visit — downloading just the video.";
    public const string MixPlaylistMessage = "That's a YouTube Mix. Mixes can't be downloaded as a playlist yet — open one of its videos instead.";
    public const string PrivatePlaylistMessage = "That playlist is private to your YouTube account, so it can't be downloaded.";

    /// <summary>Fetch one video, as today. <paramref name="Hint"/> is a one-line note for the user, or null.</summary>
    public sealed record FetchVideo(string VideoId, string? Hint) : LinkAction;

    /// <summary>A video inside a downloadable list: fetch the video, and offer "This video" / "Whole playlist".</summary>
    public sealed record OfferChoice(string VideoId, string ListId, ListKind Kind) : LinkAction;

    /// <summary>A playlist link: open the playlist card.</summary>
    public sealed record OpenPlaylist(string ListId, ListKind Kind) : LinkAction;

    public sealed record Reject(string Message) : LinkAction;

    public static LinkAction For(YouTubeLink link) => link switch
    {
        YouTubeLink.Video v => new FetchVideo(v.VideoId, null),
        YouTubeLink.VideoInPlaylist { Kind: ListKind.Mix } v => new FetchVideo(v.VideoId, MixHint),
        YouTubeLink.VideoInPlaylist { Kind: ListKind.PrivateToAccount } v => new FetchVideo(v.VideoId, PrivateListHint),
        YouTubeLink.VideoInPlaylist v => new OfferChoice(v.VideoId, v.ListId, v.Kind),
        YouTubeLink.Playlist { Kind: ListKind.Mix } => new Reject(MixPlaylistMessage),
        YouTubeLink.Playlist { Kind: ListKind.PrivateToAccount } => new Reject(PrivatePlaylistMessage),
        YouTubeLink.Playlist p => new OpenPlaylist(p.ListId, p.Kind),
        YouTubeLink.Invalid => new Reject(InvalidMessage),
        _ => throw new UnreachableException()
    };
}
