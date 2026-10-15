namespace YoutubeDownloader.Core.Services;

public interface IFolderPickerService
{
    /// <summary>
    /// Asks for a folder, starting in <paramref name="initialDirectory"/> when it exists.
    /// Returns the chosen folder's absolute path, or null if the user cancels.
    /// </summary>
    Task<string?> PickFolderAsync(string title, string? initialDirectory);
}
