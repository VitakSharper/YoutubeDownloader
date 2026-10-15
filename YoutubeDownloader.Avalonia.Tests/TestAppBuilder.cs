using Avalonia;
using Avalonia.Headless;
using YoutubeDownloader.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace YoutubeDownloader.Avalonia.Tests;

/// <summary>
/// The real <see cref="App"/>: its Initialize loads App.axaml (Fluent theme, brushes, styles), so views
/// render as in the shipped app. The startup work (DI, crash handlers, main window) is skipped.
/// </summary>
public sealed class TestApp : App
{
    public override void OnFrameworkInitializationCompleted() { }
}

public static class TestAppBuilder
{
    // Real Skia rendering (UseHeadlessDrawing = false) so CaptureRenderedFrame returns actual pixels.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
