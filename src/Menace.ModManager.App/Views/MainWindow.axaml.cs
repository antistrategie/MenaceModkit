using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Menace.Modkit.ModManagement;
using Menace.ModManager.ViewModels;

namespace Menace.ModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
        => (DataContext as MainViewModel)?.Refresh();

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ManagedMod mod } && DataContext is MainViewModel vm)
            vm.Toggle(mod);
    }
}
