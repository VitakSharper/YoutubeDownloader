using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AvaloniaClipboardService _clipboard;
    private bool _closingAfterQueueStop;

    public MainWindow() // designer ctor
    {
        AvaloniaXamlLoader.Load(this);
        _vm = null!;
        _clipboard = null!;
    }

    public MainWindow(MainViewModel vm, AvaloniaClipboardService clipboard, UpdateViewModel update) : this()
    {
        _vm = vm;
        _clipboard = clipboard;
        DataContext = vm;

        // Set the update panel's DataContext AFTER XAML load (Load runs in the chained
        // designer ctor). DataContext raises change notification, so the header's
        // Check-for-updates / Download bindings resolve. We resolve the panel via the
        // NameScope (FindControl) rather than the generated field: this view uses the
        // runtime loader (AvaloniaXamlLoader.Load(this)), which populates the NameScope
        // but does NOT assign the strongly-typed code-behind fields (they stay null).
        this.FindControl<StackPanel>("UpdatePanel")!.DataContext = update;

        // One silent update check per launch; it only speaks up when a newer release exists.
        Opened += async (_, _) => await update.CheckSilentlyAsync();

        // Fires on first show (covers startup) and on every refocus.
        // Refresh the clipboard cache first, then let the VM inspect it.
        Activated += async (_, _) =>
        {
            await _clipboard.RefreshAsync();
            _vm.CheckClipboardCommand.Execute(null);
        };

        // 018 §7: closing mid-run must not leave .part and temp files behind. Hold the close, stop the queue
        // (its items discard their files), then close for real; a queue that hangs gets 3 s at most.
        Closing += async (_, e) =>
        {
            if (_closingAfterQueueStop || !_vm.IsQueueRunning)
                return;
            e.Cancel = true;
            _closingAfterQueueStop = true;
            await _vm.StopQueueAsync(TimeSpan.FromSeconds(3));
            Close();
        };
    }
}
