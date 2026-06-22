using System.Globalization;
using System.Text.RegularExpressions;

namespace YoutubeDownloader.Core.Ffmpeg;

public static partial class FFmpegProgressParser
{
    [GeneratedRegex(@"time=(\d+):(\d{2}):(\d{2})\.(\d+)")]
    private static partial Regex TimeRegex();

    public static bool TryParseTimeSeconds(string line, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(line))
            return false;

        var m = TimeRegex().Match(line);
        if (!m.Success)
            return false;

        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var sec = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var frac = double.Parse("0." + m.Groups[4].Value, CultureInfo.InvariantCulture);

        seconds = (h * 3600) + (min * 60) + sec + frac;
        return true;
    }
}
