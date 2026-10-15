namespace YoutubeDownloader.Core.Helpers;

public static class ThumbnailPicker
{
    /// <summary>
    /// The smallest thumbnail at least <paramref name="minWidth"/> pixels wide; if none is that
    /// wide, the largest one. Returns "" when there is no usable thumbnail.
    /// </summary>
    public static string Pick(IEnumerable<(string Url, int Width, int Height)> thumbnails, int minWidth)
    {
        var usable = thumbnails.Where(t => !string.IsNullOrWhiteSpace(t.Url)).ToList();

        var smallestSharp = usable
            .Where(t => t.Width >= minWidth)
            .OrderBy(t => t.Width)
            .ThenBy(t => t.Height)
            .Select(t => t.Url)
            .FirstOrDefault();

        return smallestSharp
            ?? usable.OrderByDescending(t => (long)t.Width * t.Height).Select(t => t.Url).FirstOrDefault()
            ?? "";
    }
}
