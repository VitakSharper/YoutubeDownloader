using System.Diagnostics;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

/// <summary>Opens a URL with the OS default handler (the user's browser).</summary>
public sealed class ProcessLinkOpener : ILinkOpener
{
    public void Open(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
