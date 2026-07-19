using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Menace.Modkit.ModManagement;
using Menace.ModManager.ViewModels;

namespace Menace.ModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Vm?.Refresh();

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ManagedMod mod })
            Vm?.Toggle(mod);
    }

    private async void OnAddModClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a mod archive or DLL to install",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mods (archive or DLL)")
                {
                    Patterns = new[] { "*.zip", "*.7z", "*.rar", "*.tar", "*.tar.gz", "*.tgz", "*.dll" },
                },
                FilePickerFileTypes.All,
            },
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
            Vm?.Install(path);
    }

    private async void OnGetJiangyuClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await vm.InstallJiangyuLoaderAsync();
    }

    private async void OnInstallMelonLoaderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await vm.InstallMelonLoaderAsync();
    }

    private async void OnInstallModpackLoaderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await vm.InstallModpackLoaderAsync();
    }

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Selected is not { } mod)
            return;

        if (!mod.CanToggle)
        {
            Vm.Refresh(); // no-op refresh; protected mods can't be removed
            return;
        }

        var confirmed = await new ConfirmDialog(
            $"Delete \"{mod.DisplayName}\" from disk?\n\nThis removes its files and cannot be undone.")
            .ShowDialog<bool>(this);

        if (confirmed)
            Vm.UninstallSelected();
    }
}
