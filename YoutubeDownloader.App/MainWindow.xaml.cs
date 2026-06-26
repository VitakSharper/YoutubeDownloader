using System.Windows;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fires on first show (covers startup) and on every refocus.
        Activated += (_, _) => (DataContext as MainViewModel)?.CheckClipboardCommand.Execute(null);
    }
}
