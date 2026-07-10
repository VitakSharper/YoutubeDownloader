using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace YoutubeDownloader.Avalonia.Infrastructure;

/// <summary>Resolves the single main window / its TopLevel lazily from the desktop lifetime.</summary>
internal static class TopLevelResolver
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static TopLevel? TopLevel => MainWindow is { } w ? global::Avalonia.Controls.TopLevel.GetTopLevel(w) : null;
}
