using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace YoutubeDownloader.Avalonia.Imaging;

/// <summary>
/// Attached property that async-loads an http(s) image URL into an <see cref="Image"/>.
/// Usage: &lt;Image imaging:AsyncImage.SourceUrl="{Binding VideoInfo.ThumbnailUrl}" /&gt;
/// Fails silently (leaves the image empty) on any error.
/// </summary>
public static class AsyncImage
{
    private static readonly HttpClient Http = new();

    /// <summary>Decode width: 2× the 120 px thumbnail box in MainWindow.axaml (sharp up to 200% DPI).</summary>
    private const int DecodeWidth = 240;

    // Only the most recent image is kept: the UI shows one thumbnail at a time, and the old
    // dictionary grew by one full-size bitmap per fetched video. Touched only from OnChanged,
    // which runs and resumes on the UI thread, so no locking is needed.
    private static string? _lastUrl;
    private static Bitmap? _lastBitmap;

    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("SourceUrl", typeof(AsyncImage));

    public static void SetSourceUrl(Image element, string? value) => element.SetValue(SourceUrlProperty, value);
    public static string? GetSourceUrl(Image element) => element.GetValue(SourceUrlProperty);

    static AsyncImage() => SourceUrlProperty.Changed.AddClassHandler<Image>(OnChanged);

    private static async void OnChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (url == _lastUrl && _lastBitmap is not null)
            {
                image.Source = _lastBitmap;
                return;
            }

            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bitmap = Bitmap.DecodeToWidth(ms, DecodeWidth);

            // The previous bitmap is not disposed here: an Image may still be showing it.
            // Once nothing references it, it is reclaimed by the GC.
            _lastUrl = url;
            _lastBitmap = bitmap;

            // The Avalonia sync context returns us to the UI thread; guard against a stale URL.
            if (GetSourceUrl(image) == url)
                image.Source = bitmap;
        }
        catch
        {
            // ignore — leave the image empty
        }
    }
}
