namespace YoutubeDownloader.Core.Helpers;

/// <summary>
/// Puts a downloaded exe in place of the running one. Windows lets a running exe be renamed
/// but not deleted, so the current file becomes <c>&lt;exe&gt;.old</c> and is removed on a later launch.
/// </summary>
public static class ExeSwapper
{
    /// <summary>The name the running exe is moved to during a swap.</summary>
    public static string OldPath(string currentExe) => currentExe + ".old";

    /// <summary>
    /// Moves <paramref name="currentExe"/> to <c>.old</c> (replacing a stale one), then
    /// <paramref name="newExe"/> to <paramref name="currentExe"/>. If the second move fails, the
    /// original exe is moved back before the exception propagates, so nothing is left half-done.
    /// </summary>
    public static void Swap(string currentExe, string newExe)
    {
        var old = OldPath(currentExe);
        File.Move(currentExe, old, overwrite: true);
        try
        {
            File.Move(newExe, currentExe);
        }
        catch
        {
            File.Move(old, currentExe, overwrite: true);
            throw;
        }
    }

    /// <summary>
    /// Reverses a successful <see cref="Swap"/>: the swapped-in exe goes back to <paramref name="newExe"/>
    /// and <c>.old</c> back to <paramref name="currentExe"/>. If the second move fails, the new exe is put
    /// back in place so <paramref name="currentExe"/> is never left missing.
    /// </summary>
    public static void Undo(string currentExe, string newExe)
    {
        File.Move(currentExe, newExe);
        try
        {
            File.Move(OldPath(currentExe), currentExe);
        }
        catch
        {
            File.Move(newExe, currentExe);
            throw;
        }
    }

    /// <summary>
    /// Deletes the <c>.old</c> left by a previous update, if any. Failures are swallowed: right after a
    /// self-update the previous process is usually still exiting and holds the file; the next launch retries.
    /// </summary>
    public static void CleanupOld(string? currentExe)
    {
        if (currentExe is null)
            return;

        var old = OldPath(currentExe);
        try
        {
            if (File.Exists(old))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still locked by the exiting process — leave it for the next launch.
        }
    }
}
