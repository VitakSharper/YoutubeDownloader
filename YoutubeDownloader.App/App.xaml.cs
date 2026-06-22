using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.App.Services;
using YoutubeDownloader.Core.Services;
using YoutubeDownloader.Core.ViewModels;

namespace YoutubeDownloader.App;

public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YoutubeDownloader");
        var ffmpegDir = Path.Combine(baseDir, "ffmpeg");

        var services = new ServiceCollection();
        services.AddSingleton<IYouTubeService, YoutubeExplodeService>();
        services.AddSingleton<IFFmpegBinaryDownloader, XabeFFmpegBinaryDownloader>();
        services.AddSingleton<IFFmpegLocator>(sp =>
            new FFmpegLocator(sp.GetRequiredService<IFFmpegBinaryDownloader>(), ffmpegDir));
        services.AddSingleton<IMediaConverter, FFmpegMediaConverter>();
        services.AddSingleton<ITempFileService, TempFileService>();
        services.AddSingleton<ISaveFileService, SaveFileService>();
        services.AddSingleton<IHistoryStore>(_ => new JsonHistoryStore(baseDir));
        services.AddSingleton<ILinkOpener, ProcessLinkOpener>();
        services.AddSingleton<IFileRevealer, ExplorerFileRevealer>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();

        var window = _provider.GetRequiredService<MainWindow>();
        window.DataContext = _provider.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.Dispose();
        base.OnExit(e);
    }
}
