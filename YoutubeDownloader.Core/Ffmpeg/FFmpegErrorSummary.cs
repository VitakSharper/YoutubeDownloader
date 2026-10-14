using System.Text.RegularExpressions;

namespace YoutubeDownloader.Core.Ffmpeg;

/// <summary>Picks the informative tail of FFmpeg's stderr for an error message.</summary>
public static partial class FFmpegErrorSummary
{
    private const int MaxLines = 2;

    // Stats lines ("size=  256kB time=00:00:10.00 bitrate=...", "frame=  120 fps=...").
    [GeneratedRegex(@"^\s*(frame|size)=")]
    private static partial Regex ProgressLineRegex();

    // Generic trailers that carry no cause on their own.
    private static readonly string[] GenericLines =
    {
        "Conversion failed!",
        "Exiting normally, received signal 2.",
        "Press [q] to stop, [?] for help"
    };

    /// <summary>
    /// Returns the last (up to two) meaningful stderr lines joined with " | ",
    /// truncated to <paramref name="maxLength"/> characters (ending with "…"), or "" when none.
    /// </summary>
    public static string From(IEnumerable<string> stderrLines, int maxLength = 200)
    {
        var meaningful = stderrLines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Where(l => !ProgressLineRegex().IsMatch(l))
            .Where(l => !GenericLines.Contains(l, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var summary = string.Join(" | ", meaningful.Skip(Math.Max(0, meaningful.Count - MaxLines)));
        return summary.Length <= maxLength ? summary : summary[..(maxLength - 1)] + "…";
    }
}
