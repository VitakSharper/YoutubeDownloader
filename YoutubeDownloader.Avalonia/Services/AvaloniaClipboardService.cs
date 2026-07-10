using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

/// <summary>
/// Avalonia clipboard is async-only; <see cref="IClipboardService.GetText"/> must be synchronous.
/// So we cache the clipboard text (refreshed on window activation) and return the snapshot.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private volatile string? _cached;

    public string? GetText() => _cached;

    /// <summary>Read the OS clipboard and update the cached snapshot. Call on window activation.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var clipboard = TopLevelResolver.TopLevel?.Clipboard;
            _cached = clipboard is null ? null : await clipboard.GetTextAsync();
        }
        catch
        {
            _cached = null; // clipboard can be momentarily locked; treat as empty
        }
    }
}
