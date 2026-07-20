using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using YoutubeDownloader.Avalonia.Views;

namespace YoutubeDownloader.Avalonia.Tests;

public class ConfirmDialogTests
{
    // Regression guard: the dialog is built via AvaloniaXamlLoader.Load(this), which populates
    // the NameScope but NOT the generated x:Name fields. Touching those fields in the ctor threw
    // a NullReferenceException, crashing the app whenever a confirm dialog was needed
    // (Clear all / Cancel download). Constructing it must not throw, and the text must be set.
    [AvaloniaFact]
    public void Constructing_WithTitleAndMessage_DoesNotThrow_AndPopulatesContent()
    {
        var dialog = new ConfirmDialog("Clear history?", "Clear all download history? This can't be undone.");

        Assert.Equal("Clear history?", dialog.Title);
        Assert.Equal("Clear history?", dialog.FindControl<TextBlock>("TitleText")!.Text);
        Assert.Equal("Clear all download history? This can't be undone.", dialog.FindControl<TextBlock>("MessageText")!.Text);
        Assert.NotNull(dialog.FindControl<Button>("YesButton"));
        Assert.NotNull(dialog.FindControl<Button>("NoButton"));
    }
}
