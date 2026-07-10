using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader.Avalonia.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() // parameterless ctor for the XAML loader / designer
    {
        AvaloniaXamlLoader.Load(this);
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void InitializeComponent() { } // referenced controls are wired via x:Name after Load
}
