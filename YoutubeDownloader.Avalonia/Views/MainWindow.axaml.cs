using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AvaloniaClipboardService _clipboard;

    public MainWindow() // designer ctor
    {
        AvaloniaXamlLoader.Load(this);
        _vm = null!;
        _clipboard = null!;
        Update = null!;
    }

    public MainWindow(MainViewModel vm, AvaloniaClipboardService clipboard, UpdateViewModel update) : this()
    {
        _vm = vm;
        _clipboard = clipboard;
        Update = update;
        DataContext = vm;

        // Fires on first show (covers startup) and on every refocus.
        // Refresh the clipboard cache first, then let the VM inspect it.
        Activated += async (_, _) =>
        {
            await _clipboard.RefreshAsync();
            _vm.CheckClipboardCommand.Execute(null);
        };
    }

    /// <summary>Update-check VM, surfaced for the header controls (bound via #Root.Update).</summary>
    public UpdateViewModel Update { get; }
}
