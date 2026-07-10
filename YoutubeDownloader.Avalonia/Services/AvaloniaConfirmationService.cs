using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

public sealed class AvaloniaConfirmationService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(string message, string title)
    {
        var owner = TopLevelResolver.MainWindow;
        if (owner is null)
            return false;

        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }
}
