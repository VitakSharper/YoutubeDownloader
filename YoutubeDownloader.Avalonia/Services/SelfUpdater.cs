using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>
/// Installs a downloaded release over the running single-file exe: the running file is renamed to
/// <c>.old</c> (Windows allows renaming a running exe, not deleting it), the new exe takes its name,
/// is started, and this instance exits. The next launch deletes <c>.old</c>.
/// </summary>
public sealed class SelfUpdater : ISelfUpdater
{
    private readonly string? _processPath;

    /// <param name="processPath">
    /// <see cref="Environment.ProcessPath"/> read once at startup: after a rename its value is undefined,
    /// so the path is never re-derived later.
    /// </param>
    public SelfUpdater(string? processPath) => _processPath = processPath;

    public bool CanSelfUpdate =>
        !IsSharedDotnetHost(_processPath)
        && SelfUpdateEligibility.IsEligible(_processPath, Debugger.IsAttached, File.Exists, CanWrite);

    public void ApplyAndRestart(string newExePath)
    {
        if (_processPath is null)
            throw new InvalidOperationException("The running executable's path is unknown.");

        ExeSwapper.Swap(_processPath, newExePath);
        try
        {
            Process.Start(new ProcessStartInfo(_processPath) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            // The new exe could not be started (e.g. blocked by antivirus): put both files back
            // so the caller's manual fallback ("replace the .exe yourself") stays accurate.
            ExeSwapper.Undo(_processPath, newExePath);
            throw;
        }

        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // "dotnet app.dll" runs from the shared host's folder; never let a swap replace dotnet.exe.
    private static bool IsSharedDotnetHost(string? processPath) =>
        string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, $".ydl-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
