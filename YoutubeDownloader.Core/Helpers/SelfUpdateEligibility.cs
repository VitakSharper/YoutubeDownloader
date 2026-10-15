namespace YoutubeDownloader.Core.Helpers;

/// <summary>Decides whether the running build may replace its own exe (pure: file-system checks are injected).</summary>
public static class SelfUpdateEligibility
{
    /// <summary>The main assembly sits beside the exe only in a framework-dependent (dev) build, never in the single-file release.</summary>
    public const string AppDllName = "LiudochkaYoutubeDownloader.Avalonia.dll";

    /// <summary>
    /// True only for a single-file exe at a known path, without a debugger, in a folder the user can write to.
    /// </summary>
    public static bool IsEligible(
        string? processPath,
        bool debuggerAttached,
        Func<string, bool> fileExists,
        Func<string, bool> canWriteToFolder)
    {
        if (processPath is null || debuggerAttached)
            return false;

        var dir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(dir))
            return false;

        if (fileExists(Path.Combine(dir, AppDllName)))
            return false;

        return canWriteToFolder(dir);
    }
}
