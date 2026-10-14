namespace YoutubeDownloader.Core.Helpers;

public static class PartFilePath
{
    /// <summary>
    /// Sibling path FFmpeg writes to until the result is committed: "C:\out\song.mp3" → "C:\out\song.part.mp3".
    /// The real extension stays last so FFmpeg still picks the right muxer from it.
    /// </summary>
    public static string For(string target)
    {
        var dir = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        return Path.Combine(dir, $"{name}.part{ext}");
    }
}
