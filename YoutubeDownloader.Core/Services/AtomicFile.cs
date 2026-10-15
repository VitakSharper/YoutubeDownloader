namespace YoutubeDownloader.Core.Services;

/// <summary>Crash-safe file writes and recovery for the small JSON stores.</summary>
internal static class AtomicFile
{
    /// <summary>Writes to "&lt;path&gt;.tmp" then renames over <paramref name="path"/>, so a crash never leaves a half-written file.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Best-effort: moves an unreadable file to "&lt;path&gt;.corrupt-yyyyMMdd-HHmmss" so a later save can't destroy it. Never throws.</summary>
    public static void Quarantine(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch
        {
            // Locked or already gone: leave it where it is.
        }
    }
}
