using System.Collections.ObjectModel;
using System.Linq;
using Menace.Modkit.ModManagement;
using ReactiveUI;

namespace Menace.ModManager.ViewModels;

/// <summary>
/// Backs the main window: scans <c>Mods/</c> via <see cref="ModCatalog"/> and exposes the
/// unified mod list. Stateless — <see cref="Refresh"/> re-reads the filesystem each call.
/// </summary>
public sealed class MainViewModel : ReactiveObject
{
    private readonly ModCatalog _catalog = new();
    private readonly ModEnableService _enableService = new();
    private readonly ModInstallService _installService = new();

    public ObservableCollection<ManagedMod> Mods { get; } = new();

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private ManagedMod? _selected;
    public ManagedMod? Selected
    {
        get => _selected;
        set => this.RaiseAndSetIfChanged(ref _selected, value);
    }

    public MainViewModel() => Refresh();

    public void Refresh()
    {
        Mods.Clear();
        foreach (var mod in _catalog.Scan().OrderBy(m => m.Kind).ThenBy(m => m.DisplayName))
            Mods.Add(mod);

        var path = _catalog.ModsPath;
        Status = path == null
            ? "Game not located — set MENACE_GAME_PATH."
            : $"{Mods.Count} mod(s) — {path}";
    }

    /// <summary>Flip a mod's enabled state (move between Mods/ and DisabledMods/), then rescan.</summary>
    public void Toggle(ManagedMod mod)
    {
        if (!mod.CanToggle)
            return;

        try
        {
            _enableService.SetEnabled(mod, !mod.IsEnabled);
        }
        catch (Exception ex)
        {
            Status = $"Failed to toggle {mod.DisplayName}: {ex.Message}";
            return;
        }

        Refresh();
    }

    /// <summary>Install a mod from a source folder or .dll, then rescan.</summary>
    public void Install(string sourcePath)
    {
        try
        {
            var installed = _installService.Install(sourcePath);
            Refresh();
            Status = $"Installed {System.IO.Path.GetFileName(installed)} — {Status}";
        }
        catch (Exception ex)
        {
            Status = $"Install failed: {ex.Message}";
        }
    }

    /// <summary>Delete the selected mod from disk, then rescan.</summary>
    public void UninstallSelected()
    {
        if (Selected is not { } mod || !mod.CanToggle)
            return;

        try
        {
            _installService.Uninstall(mod);
        }
        catch (Exception ex)
        {
            Status = $"Uninstall failed: {ex.Message}";
            return;
        }

        Refresh();
    }
}
