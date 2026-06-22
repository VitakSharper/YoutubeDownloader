namespace YoutubeDownloader.Core.Services;

public interface ISaveFileService
{
    /// <summary>Returns the chosen absolute path, or null if the user cancels.</summary>
    string? PromptForSavePath(string suggestedFileName, string extension);
}
