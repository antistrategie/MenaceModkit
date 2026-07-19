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
    private readonly JiangyuLoaderInstaller _jiangyu = new();

    public ObservableCollection<ManagedMod> Mods { get; } = new();

    /// <summary>Jiangyu loader versions offered in the picker ("latest" first, then release tags).</summary>
    public ObservableCollection<string> JiangyuVersions { get; } = new() { "latest" };

    private string? _selectedJiangyuVersion = "latest";
    public string? SelectedJiangyuVersion
    {
        get => _selectedJiangyuVersion;
        set => this.RaiseAndSetIfChanged(ref _selectedJiangyuVersion, value);
    }

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

    public MainViewModel()
    {
        Refresh();
        // Populate the Jiangyu version list in the background; "latest" works regardless.
        _ = LoadJiangyuVersionsAsync();
    }

    private async System.Threading.Tasks.Task LoadJiangyuVersionsAsync()
    {
        try
        {
            var versions = await _jiangyu.ListVersionsAsync();
            foreach (var v in versions)
                if (!JiangyuVersions.Contains(v))
                    JiangyuVersions.Add(v);
        }
        catch
        {
            // Offline or rate-limited — the "latest" option still installs on demand.
        }
    }

    /// <summary>Download + install the selected Jiangyu loader version into Mods/, then rescan.</summary>
    public async System.Threading.Tasks.Task InstallJiangyuLoaderAsync()
    {
        var version = string.IsNullOrEmpty(SelectedJiangyuVersion) || SelectedJiangyuVersion == "latest"
            ? null
            : SelectedJiangyuVersion;

        Status = version is null ? "Downloading latest Jiangyu loader…" : $"Downloading Jiangyu loader {version}…";
        try
        {
            await _jiangyu.InstallAsync(version);
        }
        catch (Exception ex)
        {
            Status = $"Jiangyu loader install failed: {ex.Message}";
            return;
        }

        Refresh();
        Status = $"Jiangyu loader installed — {Status}";
    }

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
