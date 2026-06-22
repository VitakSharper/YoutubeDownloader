using System.Globalization;

namespace YoutubeDownloader.Core.Helpers;

public static class HistoryDateLabel
{
    /// <summary>Human-friendly group header for a date relative to today.</summary>
    public static string For(DateOnly date, DateOnly today)
    {
        if (date == today)
            return "Today";
        if (date == today.AddDays(-1))
            return "Yesterday";
        return date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
    }
}
