using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader.Avalonia.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() // parameterless ctor for the XAML loader / designer
    {
        AvaloniaXamlLoader.Load(this);
        // Load populates the NameScope but NOT the generated x:Name fields (they stay null),
        // so resolve controls via FindControl — matching the pattern in MainWindow.
        this.FindControl<Button>("YesButton")!.Click += (_, _) => Close(true);
        this.FindControl<Button>("NoButton")!.Click += (_, _) => Close(false);
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }
}
