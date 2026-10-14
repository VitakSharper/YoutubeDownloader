namespace YoutubeDownloader.Core.Services;

public sealed class FFmpegLocator : IFFmpegLocator
{
    private readonly IFFmpegBinaryDownloader _downloader;
    private readonly string _installDir;
    private readonly ProgressRelay _relay = new();
    private readonly object _gate = new();
    private Task<string>? _install;

    public FFmpegLocator(IFFmpegBinaryDownloader downloader, string installDir)
    {
        _downloader = downloader;
        _installDir = installDir;
    }

    public bool IsAvailable => File.Exists(FfmpegPath);

    private string FfmpegPath => Path.Combine(_installDir, "ffmpeg.exe");

    public async Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (File.Exists(FfmpegPath))
        {
            progress?.Report(1.0);
            return FfmpegPath;
        }

        _relay.Target = progress;
        try
        {
            var install = GetOrStartInstall();
            // Cancelling only stops waiting: the install keeps running in the background for the next attempt.
            return await install.WaitAsync(ct);
        }
        finally
        {
            _relay.ClearIfCurrent(progress);
        }
    }

    private Task<string> GetOrStartInstall()
    {
        lock (_gate)
        {
            if (_install is null || _install.IsCompleted)
                _install = Task.Run(InstallAsync);
            return _install;
        }
    }

    private async Task<string> InstallAsync()
    {
        var trimmed = _installDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DeleteStaleStagingFolders(trimmed);

        var staging = trimmed + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            await _downloader.DownloadAsync(staging, _relay, CancellationToken.None);

            if (!File.Exists(Path.Combine(staging, "ffmpeg.exe")))
                throw new FileNotFoundException("FFmpeg download completed but ffmpeg.exe was not found.", FfmpegPath);

            Publish(staging);
            return FfmpegPath;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private void Publish(string staging)
    {
        Directory.CreateDirectory(_installDir);
        foreach (var file in Directory.GetFiles(staging))
        {
            var name = Path.GetFileName(file);
            if (!name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                File.Move(file, Path.Combine(_installDir, name), overwrite: true);
        }

        // ffmpeg.exe last: its presence is the "install complete" marker.
        File.Move(Path.Combine(staging, "ffmpeg.exe"), FfmpegPath, overwrite: true);
    }

    private static void DeleteStaleStagingFolders(string trimmedInstallDir)
    {
        try
        {
            var parent = Path.GetDirectoryName(trimmedInstallDir);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return;

            var pattern = Path.GetFileName(trimmedInstallDir) + ".staging-*";
            foreach (var stale in Directory.GetDirectories(parent, pattern))
                TryDeleteDirectory(stale);
        }
        catch
        {
            // best effort
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class ProgressRelay : IProgress<double>
    {
        private volatile IProgress<double>? _target;

        public IProgress<double>? Target
        {
            set => _target = value;
        }

        public void Report(double value) => _target?.Report(value);

        public void ClearIfCurrent(IProgress<double>? owner)
        {
            if (ReferenceEquals(_target, owner))
                _target = null;
        }
    }
}
