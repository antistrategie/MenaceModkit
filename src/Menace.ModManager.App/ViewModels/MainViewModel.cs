using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Menace.Modkit.App.Services; // ModLoaderInstaller (kept its original namespace when extracted)
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
    private readonly MelonLoaderInstaller _melonLoader = new();

    public ObservableCollection<ManagedMod> Mods { get; } = new();

    /// <summary>MelonLoader versions offered in the picker ("latest" first, then release tags).</summary>
    public ObservableCollection<string> MelonLoaderVersions { get; } = new() { "latest" };

    private string? _selectedMelonLoaderVersion = "latest";
    public string? SelectedMelonLoaderVersion
    {
        get => _selectedMelonLoaderVersion;
        set => this.RaiseAndSetIfChanged(ref _selectedMelonLoaderVersion, value);
    }

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

    private string _melonLoaderStatus = string.Empty;
    public string MelonLoaderStatus
    {
        get => _melonLoaderStatus;
        private set => this.RaiseAndSetIfChanged(ref _melonLoaderStatus, value);
    }

    private string _modpackLoaderStatus = string.Empty;
    public string ModpackLoaderStatus
    {
        get => _modpackLoaderStatus;
        private set => this.RaiseAndSetIfChanged(ref _modpackLoaderStatus, value);
    }

    private string _jiangyuLoaderStatus = string.Empty;
    public string JiangyuLoaderStatus
    {
        get => _jiangyuLoaderStatus;
        private set => this.RaiseAndSetIfChanged(ref _jiangyuLoaderStatus, value);
    }

    public MainViewModel()
    {
        Refresh();
        // Populate the version lists in the background; "latest" works regardless.
        _ = LoadVersionListAsync(_melonLoader.ListVersionsAsync, MelonLoaderVersions);
        _ = LoadVersionListAsync(_jiangyu.ListVersionsAsync, JiangyuVersions);
    }

    private static async Task LoadVersionListAsync(
        Func<CancellationToken, Task<IReadOnlyList<string>>> list, ObservableCollection<string> into)
    {
        try
        {
            foreach (var v in await list(CancellationToken.None))
                if (!into.Contains(v))
                    into.Add(v);
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

    /// <summary>Download + install the selected MelonLoader version into the game.</summary>
    public async Task InstallMelonLoaderAsync()
    {
        var version = string.IsNullOrEmpty(SelectedMelonLoaderVersion) || SelectedMelonLoaderVersion == "latest"
            ? null
            : SelectedMelonLoaderVersion;

        Status = version is null ? "Downloading latest MelonLoader…" : $"Downloading MelonLoader {version}…";
        try
        {
            await _melonLoader.InstallAsync(version);
        }
        catch (Exception ex)
        {
            Status = $"MelonLoader install failed: {ex.Message}";
            return;
        }

        Refresh();
        Status = $"MelonLoader installed — {Status}";
    }

    /// <summary>Install/update the Menace Modpack Loader runtime (bundled) into the game.</summary>
    public Task InstallModpackLoaderAsync() =>
        RunLoaderInstall("Modpack Loader", (installer, cb) => installer.InstallModpackLoaderAsync(cb));

    private async Task RunLoaderInstall(string label, Func<ModLoaderInstaller, Action<string>, Task<bool>> install)
    {
        var gamePath = ModkitConfig.Current.GameInstallPath;
        if (string.IsNullOrEmpty(gamePath))
        {
            Status = "Game not located — set MENACE_GAME_PATH.";
            return;
        }

        Status = $"Installing {label}…";
        bool ok;
        try
        {
            // Run off the UI thread; marshal progress messages back to it.
            ok = await Task.Run(() => install(
                new ModLoaderInstaller(gamePath),
                msg => Dispatcher.UIThread.Post(() => Status = msg)));
        }
        catch (Exception ex)
        {
            Status = $"{label} install failed: {ex.Message}";
            return;
        }

        Refresh();
        Status = ok ? $"{label} installed." : $"{label} install failed (see modkit.log).";
    }

    public void Refresh()
    {
        Mods.Clear();
        foreach (var mod in _catalog.Scan().OrderBy(m => KindRank(m.Kind)).ThenBy(m => m.DisplayName))
            Mods.Add(mod);

        var path = _catalog.ModsPath;
        Status = path == null
            ? "Game not located — set MENACE_GAME_PATH."
            : $"{Mods.Count} mod(s) — {path}";

        RefreshLoaderStatuses();
    }

    // Infrastructure (loaders) first, then modpacks, Jiangyu mods, raw MelonMods.
    private static int KindRank(ModKind kind) => kind switch
    {
        ModKind.Infrastructure => 0,
        ModKind.Modpack => 1,
        ModKind.Jiangyu => 2,
        ModKind.MelonMod => 3,
        _ => 4,
    };

    private void RefreshLoaderStatuses()
    {
        var gamePath = ModkitConfig.Current.GameInstallPath;
        if (string.IsNullOrEmpty(gamePath))
        {
            MelonLoaderStatus = ModpackLoaderStatus = JiangyuLoaderStatus = "game not located";
            return;
        }

        // MelonLoader lives at the game root (version.dll + MelonLoader/), not in Mods/,
        // so query the installer rather than the scanned list.
        var installer = new ModLoaderInstaller(gamePath);
        var melonVersion = installer.GetInstalledMelonLoaderVersion();
        MelonLoaderStatus = installer.IsMelonLoaderInstalled()
            ? $"installed{(string.IsNullOrEmpty(melonVersion) ? "" : $" {melonVersion}")}"
            : "not installed";

        // The other two appear in the scan as infrastructure DLLs in Mods/.
        ModpackLoaderStatus = DescribeInstalled("Menace.ModpackLoader.dll");
        JiangyuLoaderStatus = DescribeInstalled("Jiangyu.Loader.dll");
    }

    private string DescribeInstalled(string dllId)
    {
        var mod = Mods.FirstOrDefault(m => string.Equals(m.Id, dllId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            return "not installed";
        return string.IsNullOrEmpty(mod.Version) ? "installed" : $"installed {mod.Version}";
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
