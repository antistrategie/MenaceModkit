using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Menace.ModManager.ViewModels;

namespace Menace.ModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
        => (DataContext as MainViewModel)?.Refresh();
}
