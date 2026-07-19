using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Menace.ModManager.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => AvaloniaXamlLoader.Load(this);

    public ConfirmDialog(string message) : this()
    {
        var text = this.FindControl<TextBlock>("MessageText");
        if (text != null)
            text.Text = message;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
