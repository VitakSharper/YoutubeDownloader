namespace YoutubeDownloader.Core.Models;

/// <summary>A "max video height" choice for the queue. Above 1080p, YouTube serves MP4 only as AV1.</summary>
public sealed record VideoHeightCap(int MaxHeight, string Label)
{
    /// <summary>1080p: the tallest height YouTube still serves as H.264 in MP4 (maintainer answer 11).</summary>
    public static readonly VideoHeightCap Default = new(1080, "1080p");

    /// <summary>All caps in dropdown order (tallest first).</summary>
    public static readonly IReadOnlyList<VideoHeightCap> All =
    [
        new(2160, "2160p (AV1)"), new(1440, "1440p (AV1)"), Default, new(720, "720p"), new(480, "480p"), new(360, "360p")
    ];

    public override string ToString() => Label;
}
