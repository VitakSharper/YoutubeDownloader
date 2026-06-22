using System.Diagnostics;
using System.IO;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

/// <summary>Opens Windows Explorer, selecting the file when it still exists.</summary>
public sealed class ExplorerFileRevealer : IFileRevealer
{
    public void RevealInFolder(string filePath)
    {
        if (File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
