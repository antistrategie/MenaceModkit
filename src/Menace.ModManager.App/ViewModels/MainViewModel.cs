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
    private readonly ModDeployService _deployService = new();
    private readonly JiangyuLoaderInstaller _jiangyu = new();
    private readonly MelonLoaderInstaller _melonLoader = new();

    public ObservableCollection<ManagedMod> Mods { get; } = new();

    private const string LatestSuffix = " (latest)";

    /// <summary>MelonLoader versions offered in the picker (newest first, tagged "(latest)").</summary>
    public ObservableCollection<string> MelonLoaderVersions { get; } = new();

    private string? _selectedMelonLoaderVersion;
    public string? SelectedMelonLoaderVersion
    {
        get => _selectedMelonLoaderVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMelonLoaderVersion, value);
            this.RaisePropertyChanged(nameof(CanGetMelonLoader));
        }
    }

    /// <summary>Jiangyu loader versions offered in the picker (newest first, tagged "(latest)").</summary>
    public ObservableCollection<string> JiangyuVersions { get; } = new();

    private string? _selectedJiangyuVersion;
    public string? SelectedJiangyuVersion
    {
        get => _selectedJiangyuVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedJiangyuVersion, value);
            this.RaisePropertyChanged(nameof(CanGetJiangyu));
        }
    }

    private string? _melonLoaderInstalledVersion;
    private string? _jiangyuInstalledVersion;

    /// <summary>Get is enabled only when the picked version differs from what's installed.</summary>
    public bool CanGetMelonLoader => !SameVersion(_melonLoaderInstalledVersion, NormalizeVersion(SelectedMelonLoaderVersion));
    public bool CanGetJiangyu => !SameVersion(_jiangyuInstalledVersion, NormalizeVersion(SelectedJiangyuVersion));

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

    private bool _isBusy;
    /// <summary>True while a long operation runs — drives the busy overlay and disables controls.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(NotBusy));
        }
    }
    public bool NotBusy => !_isBusy;

    private string? _errorMessage;
    /// <summary>Last operation error, shown prominently until dismissed or the next op succeeds.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);
    public void DismissError() => ErrorMessage = null;

    public MainViewModel()
    {
        Refresh();
        // Populate the version lists in the background (network); newest tagged "(latest)".
        _ = LoadVersionListAsync(_melonLoader.ListVersionsAsync, MelonLoaderVersions, v => SelectedMelonLoaderVersion = v);
        _ = LoadVersionListAsync(_jiangyu.ListVersionsAsync, JiangyuVersions, v => SelectedJiangyuVersion = v);
    }

    private static async Task LoadVersionListAsync(
        Func<CancellationToken, Task<IReadOnlyList<string>>> list,
        ObservableCollection<string> into,
        Action<string> selectDefault)
    {
        try
        {
            var versions = await list(CancellationToken.None);
            for (var i = 0; i < versions.Count; i++)
                into.Add(i == 0 ? versions[i] + LatestSuffix : versions[i]);

            if (into.Count > 0)
                selectDefault(into[0]);
        }
        catch
        {
            // Offline or rate-limited — leave empty; install-latest still works via the API.
        }
    }

    // Strip the "(latest)" label to recover the real tag; null → install latest.
    private static string? NormalizeVersion(string? selected)
    {
        if (string.IsNullOrEmpty(selected))
            return null;
        var tag = selected.EndsWith(LatestSuffix, StringComparison.Ordinal)
            ? selected[..^LatestSuffix.Length]
            : selected;
        return string.IsNullOrEmpty(tag) ? null : tag;
    }

    /// <summary>
    /// Run a long operation with the busy overlay up: clears any error, shows a status,
    /// surfaces failures as <see cref="ErrorMessage"/>, and rescans when done.
    /// </summary>
    private async Task ExecuteAsync(string busyStatus, Func<Task> work)
    {
        ErrorMessage = null;
        Status = busyStatus;
        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        Refresh();
    }

    /// <summary>Download + install the selected Jiangyu loader version into Mods/.</summary>
    public Task InstallJiangyuLoaderAsync()
    {
        var version = NormalizeVersion(SelectedJiangyuVersion);
        return ExecuteAsync(
            version is null ? "Downloading latest Jiangyu loader…" : $"Downloading Jiangyu loader {version}…",
            () => _jiangyu.InstallAsync(version));
    }

    /// <summary>Download + install the selected MelonLoader version into the game.</summary>
    public Task InstallMelonLoaderAsync()
    {
        var version = NormalizeVersion(SelectedMelonLoaderVersion);
        return ExecuteAsync(
            version is null ? "Downloading latest MelonLoader…" : $"Downloading MelonLoader {version}…",
            () => _melonLoader.InstallAsync(version));
    }

    /// <summary>Install/update the Menace Modpack Loader runtime (bundled) into the game.</summary>
    public Task InstallModpackLoaderAsync()
    {
        var gamePath = ModkitConfig.Current.GameInstallPath;
        if (string.IsNullOrEmpty(gamePath))
        {
            ErrorMessage = "Game not located — set MENACE_GAME_PATH.";
            return Task.CompletedTask;
        }

        return ExecuteAsync("Installing Modpack Loader…", async () =>
        {
            var ok = await Task.Run(() => new ModLoaderInstaller(gamePath)
                .InstallModpackLoaderAsync(msg => Dispatcher.UIThread.Post(() => Status = msg)));
            if (!ok)
                throw new InvalidOperationException("Modpack Loader install failed (see modkit.log).");
        });
    }

    public void Refresh()
    {
        // Refresh runs from the constructor and after every operation, so it must never throw
        // (an unhandled throw here would fail app startup or fault an async handler).
        try
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
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't read the Mods folder: {ex.Message}";
            Status = "Scan failed.";
        }
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
            _melonLoaderInstalledVersion = _jiangyuInstalledVersion = null;
        }
        else
        {
            // MelonLoader lives at the game root (version.dll + MelonLoader/), not in Mods/,
            // so query the installer rather than the scanned list.
            var installer = new ModLoaderInstaller(gamePath);
            var installed = installer.IsMelonLoaderInstalled();
            var melonVersion = installer.GetInstalledMelonLoaderVersion();
            _melonLoaderInstalledVersion = installed ? melonVersion : null;
            MelonLoaderStatus = installed
                ? $"installed{(string.IsNullOrEmpty(melonVersion) ? "" : $" {melonVersion}")}"
                : "not installed";

            // The other two appear in the scan as infrastructure DLLs in Mods/.
            ModpackLoaderStatus = DescribeInstalled("Menace.ModpackLoader.dll");
            JiangyuLoaderStatus = DescribeInstalled("Jiangyu.Loader.dll");
            _jiangyuInstalledVersion = Mods
                .FirstOrDefault(m => string.Equals(m.Id, "Jiangyu.Loader.dll", StringComparison.OrdinalIgnoreCase))
                ?.Version;
        }

        this.RaisePropertyChanged(nameof(CanGetMelonLoader));
        this.RaisePropertyChanged(nameof(CanGetJiangyu));
    }

    /// <summary>
    /// True when an installed version and a release tag refer to the same release, tolerating
    /// a leading "v", git-describe suffixes ("1.2.3-8-g…"), and a trailing ".0" segment.
    /// </summary>
    private static bool SameVersion(string? installed, string? tag)
    {
        if (string.IsNullOrEmpty(installed) || string.IsNullOrEmpty(tag))
            return false;

        var a = installed.TrimStart('v', 'V').Split('-')[0];
        var b = tag.TrimStart('v', 'V').Split('-')[0];
        return a == b
            || a.StartsWith(b + ".", StringComparison.Ordinal)
            || b.StartsWith(a + ".", StringComparison.Ordinal);
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

        ErrorMessage = null;
        try
        {
            _enableService.SetEnabled(mod, !mod.IsEnabled);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't toggle {mod.DisplayName}: {ex.Message}";
            return;
        }

        Refresh();
    }

    /// <summary>
    /// The single install entry (button + drag-drop). Handles a mod archive, a folder, or a
    /// bare .dll, and works out for itself whether the mod needs compiling: a modpack with C#
    /// sources is compiled and deployed; everything else is copied straight in.
    /// </summary>
    public Task InstallAsync(string path)
    {
        // Progress must be created on the UI thread so its callbacks marshal Status back.
        var progress = new Progress<string>(s => Status = s);
        return ExecuteAsync(
            $"Installing {System.IO.Path.GetFileName(path)}…",
            () => Task.Run(() => InstallCoreAsync(path, progress)));
    }

    private async Task InstallCoreAsync(string path, IProgress<string> progress)
    {
        if (ModInstallService.IsArchive(path))
        {
            using var extracted = ModInstallService.ExtractArchiveToTemp(path);
            if (extracted.BareDll != null)
                _installService.Install(extracted.BareDll);
            else
                await RouteModDirAsync(extracted.ModRoot!, extracted.Name, progress);
        }
        else if (System.IO.Directory.Exists(path))
        {
            var name = System.IO.Path.GetFileName(
                path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            await RouteModDirAsync(path, name, progress);
        }
        else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            _installService.Install(path);
        }
        else
        {
            throw new NotSupportedException(
                $"Can't install '{System.IO.Path.GetFileName(path)}'. Add a mod archive (.zip/.7z/…) or a DLL.");
        }
    }

    private async Task RouteModDirAsync(string modDir, string name, IProgress<string> progress)
    {
        // A modpack goes through deploy (which compiles only if needed); anything else is a copy.
        if (System.IO.File.Exists(System.IO.Path.Combine(modDir, "modpack.json")))
            await _deployService.DeployAsync(modDir, progress);
        else
            _installService.InstallFrom(modDir, name);
    }

    /// <summary>Delete the selected mod from disk.</summary>
    public Task UninstallSelectedAsync()
    {
        if (Selected is not { } mod || !mod.CanToggle)
            return Task.CompletedTask;

        return ExecuteAsync(
            $"Uninstalling {mod.DisplayName}…",
            () => Task.Run(() => _installService.Uninstall(mod)));
    }
}
