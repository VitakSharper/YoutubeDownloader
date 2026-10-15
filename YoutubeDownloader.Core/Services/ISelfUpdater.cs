namespace YoutubeDownloader.Core.Services;

/// <summary>Replaces the running executable with a downloaded one and restarts the app.</summary>
public interface ISelfUpdater
{
    /// <summary>True when the running build can replace itself (single-file exe in a writable folder, no debugger).</summary>
    bool CanSelfUpdate { get; }

    /// <summary>Swaps in <paramref name="newExePath"/>, starts it, and asks the app to exit. Throws if the swap fails (nothing is left half-done).</summary>
    void ApplyAndRestart(string newExePath);
}
