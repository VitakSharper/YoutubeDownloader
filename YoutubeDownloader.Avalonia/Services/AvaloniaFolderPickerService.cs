using System.IO;
using Avalonia.Platform.Storage;
using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

public sealed class AvaloniaFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string title, string? initialDirectory)
    {
        var topLevel = TopLevelResolver.TopLevel;
        if (topLevel is null || !topLevel.StorageProvider.CanPickFolder)
            return null;

        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;   // empty list = cancelled
    }
}
