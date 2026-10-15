namespace YoutubeDownloader.Core.Models;

/// <summary>What a YouTube list id stands for, decided from its prefix (plan 018 §2 rule 4).</summary>
public enum ListKind
{
    /// <summary>An ordinary playlist, a YouTube Music list (RDCLAK5uy_…) or a channel's uploads (UU…).</summary>
    Playlist,
    /// <summary>A YouTube Music album (OLAK5uy_…).</summary>
    Album,
    /// <summary>An auto-generated Mix (RD…, other than RDCLAK5uy_ and RDMM): different on every visit.</summary>
    Mix,
    /// <summary>Watch later (WL), Liked videos (LL), Liked music (LM), My Mix (RDMM…): need the user's sign-in.</summary>
    PrivateToAccount
}
