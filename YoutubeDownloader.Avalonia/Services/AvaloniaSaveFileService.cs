using System.IO;
using Avalonia.Platform.Storage;
using YoutubeDownloader.Avalonia.Infrastructure;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Avalonia.Services;

public sealed class AvaloniaSaveFileService : ISaveFileService
{
    public async Task<string?> PromptForSavePathAsync(string suggestedFileName, string extension, string? initialDirectory)
    {
        var topLevel = TopLevelResolver.TopLevel;
        if (topLevel is null)
            return null;

        var fileType = SaveFileTypes.For(extension);
        var options = new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(fileType.Label) { Patterns = new[] { fileType.Pattern } }
            }
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }
}
