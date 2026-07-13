using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>Locates foobar2000 (registry App Paths, then Program Files) and launches it with a file.</summary>
public sealed class Foobar2000Launcher : IFoobar2000Launcher
{
    private const string AppPathsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\foobar2000.exe";

    private readonly string? _exePath;

    public Foobar2000Launcher() => _exePath = Resolve();

    public bool IsInstalled => _exePath is not null;

    public void Open(string filePath)
    {
        if (_exePath is null)
            return;
        Process.Start(new ProcessStartInfo(_exePath, $"\"{filePath}\"") { UseShellExecute = true });
    }

    private static string? Resolve()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(AppPathsKey);
                if (key?.GetValue(null) is string path && File.Exists(path))
                    return path;
            }
            catch
            {
                // Ignore unreadable/absent registry views and fall through to disk probing.
            }
        }

        foreach (var env in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrEmpty(root))
                continue;
            var candidate = Path.Combine(root, "foobar2000", "foobar2000.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
