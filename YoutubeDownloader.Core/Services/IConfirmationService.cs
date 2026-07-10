namespace YoutubeDownloader.Core.Services;

public interface IConfirmationService
{
    /// <summary>Ask the user to confirm an action. Returns true if confirmed.</summary>
    Task<bool> ConfirmAsync(string message, string title);
}
