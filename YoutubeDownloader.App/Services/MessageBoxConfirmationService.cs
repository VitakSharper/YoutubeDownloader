using System.Windows;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.App.Services;

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string message, string title) =>
        Task.FromResult(MessageBox.Show(message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
}
