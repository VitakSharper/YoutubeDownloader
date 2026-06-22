using Microsoft.Win32;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

public sealed class SaveFileService : ISaveFileService
{
    public string? PromptForSavePath(string suggestedFileName, string extension)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = "." + extension,
            Filter = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase)
                ? "MP3 audio (*.mp3)|*.mp3"
                : "MP4 video (*.mp4)|*.mp4",
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
