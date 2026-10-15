using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.Avalonia.Services;
using YoutubeDownloader.Avalonia.Views;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _provider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeDownloader");

        var crashLog = new CrashLog(baseDir);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            crashLog.Write("UI", e.Exception);
            e.Handled = true; // keep the app alive; the error is logged
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            crashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                crashLog.Write("AppDomain", ex);
        };

        var ffmpegDir = Path.Combine(baseDir, "ffmpeg");

        var services = new ServiceCollection();
        services.AddSingleton<IYouTubeService, YoutubeExplodeService>();
        services.AddSingleton<IFFmpegBinaryDownloader, XabeFFmpegBinaryDownloader>();
        services.AddSingleton<IFFmpegLocator>(sp =>
            new FFmpegLocator(sp.GetRequiredService<IFFmpegBinaryDownloader>(), ffmpegDir));
        services.AddSingleton<IMediaConverter, FFmpegMediaConverter>();
        services.AddSingleton<ITempFileService, TempFileService>();
        services.AddSingleton<IOutputFileService, OutputFileService>();
        services.AddSingleton<ISaveFileService, AvaloniaSaveFileService>();
        services.AddSingleton<IHistoryStore>(_ => new JsonHistoryStore(baseDir));
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(baseDir));
        services.AddSingleton<ILinkOpener, ProcessLinkOpener>();
        services.AddSingleton<IFileRevealer, ExplorerFileRevealer>();
        services.AddSingleton<AvaloniaClipboardService>();
        services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<AvaloniaClipboardService>());
        services.AddSingleton<IConfirmationService, AvaloniaConfirmationService>();
        services.AddSingleton<IFoobar2000Launcher, Foobar2000Launcher>();
        var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
        var updateHttp = new HttpClient();
        services.AddSingleton<IUpdateService>(_ =>
            new GitHubUpdateService(updateHttp, new UpdateOptions("VitakSharper", "YoutubeDownloader", currentVersion)));
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _provider.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => _provider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
