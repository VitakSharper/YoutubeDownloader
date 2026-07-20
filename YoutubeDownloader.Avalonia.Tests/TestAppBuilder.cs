using Avalonia;
using Avalonia.Headless;
using YoutubeDownloader.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace YoutubeDownloader.Avalonia.Tests;

/// <summary>
/// Minimal headless Application for constructing views under test. ConfirmDialog's XAML
/// depends only on literal colours and built-in controls, so it needs no app resources.
/// </summary>
public sealed class TestApp : Application { }

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
