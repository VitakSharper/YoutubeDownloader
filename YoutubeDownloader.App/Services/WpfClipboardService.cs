using System.Windows;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

/// <summary>Reads text from the Windows clipboard. Returns null when empty, non-text, or locked.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            // The clipboard can be momentarily locked by another process; treat as empty.
            return null;
        }
    }
}
