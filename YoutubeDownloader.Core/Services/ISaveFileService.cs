namespace YoutubeDownloader.Core.Services;

public interface ISaveFileService
{
    /// <summary>
    /// Prompts for a save path, starting in <paramref name="initialDirectory"/> when it exists.
    /// Returns the chosen absolute path, or null if the user cancels.
    /// </summary>
    string? PromptForSavePath(string suggestedFileName, string extension, string? initialDirectory);
}
