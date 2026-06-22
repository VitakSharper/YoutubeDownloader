namespace YoutubeDownloader.Core.Helpers;

public static class FileNameSanitizer
{
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "download";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        cleaned = cleaned.TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "download" : cleaned;
    }
}
