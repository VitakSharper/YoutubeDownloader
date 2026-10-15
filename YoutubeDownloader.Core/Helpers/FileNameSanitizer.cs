namespace YoutubeDownloader.Core.Helpers;

public static class FileNameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "download";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        cleaned = cleaned.TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "download" : cleaned;
    }

    /// <summary>
    /// <see cref="Sanitize(string)"/>, then cut to at most <paramref name="maxLength"/> chars (never inside a
    /// surrogate pair), trailing dots/spaces trimmed again, and a Windows device name (CON, NUL, COM1…) prefixed with "_".
    /// </summary>
    public static string Sanitize(string raw, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        var name = Sanitize(raw);
        if (name.Length > maxLength)
        {
            var cut = char.IsHighSurrogate(name[maxLength - 1]) ? maxLength - 1 : maxLength;
            name = name[..cut];
        }

        name = name.TrimEnd('.', ' ');
        if (name.Length == 0)
            name = "download";

        return ReservedNames.Contains(name) ? "_" + name : name;
    }
}
