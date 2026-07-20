using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Menace.Modkit.App.Services; // ModLoaderInstaller (kept its original namespace when extracted)
using Menace.Modkit.ModManagement;
using Menace.ModManager;
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

    /// <summary>Display rows: a <see cref="ModSection"/> header followed by its mods.</summary>
    public ObservableCollection<object> Rows { get; } = new();

    /// <summary>ListBox selection bridge — only mod rows count as a selection.</summary>
    private object? _selectedRow;
    public object? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            Selected = value as ManagedMod;
        }
    }

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
        set
        {
            this.RaiseAndSetIfChanged(ref _selected, value);
            this.RaisePropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

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

    private bool _modpackLoaderUpdateAvailable;
    /// <summary>True when the bundled Modpack Loader is newer than the installed one.</summary>
    public bool ModpackLoaderUpdateAvailable
    {
        get => _modpackLoaderUpdateAvailable;
        private set => this.RaiseAndSetIfChanged(ref _modpackLoaderUpdateAvailable, value);
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

    /// <summary>Surface an error from outside the VM (e.g. a faulted UI event handler).</summary>
    public void ReportError(string message) => ErrorMessage = message;

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
    /// surfaces failures as <see cref="ErrorMessage"/>, and rescans when done. Reentrancy
    /// is rejected here at the VM — the code-behind's IsBusy checks race their own awaits
    /// (e.g. a second Ctrl+V inside the clipboard-read window), and two concurrent
    /// installs would race ClearTarget/copy on the same target.
    /// </summary>
    private async Task ExecuteAsync(string busyStatus, Func<Task> work)
    {
        if (IsBusy)
            return;

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
            ErrorMessage = "Game not located — click Locate game… (or set MENACE_GAME_PATH).";
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

    /// <summary>Apply a user-chosen game folder (validated + persisted), then rescan.</summary>
    public void SetGamePath(string path)
    {
        try
        {
            if (ModkitConfig.Current is StandaloneModkitConfig config)
                config.SetGamePath(path);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        Refresh();
    }

    // Refresh coalescing state — only ever touched on the UI thread.
    private bool _refreshRunning;
    private bool _refreshPending;

    /// <summary>
    /// Rescan <c>Mods/</c> and update the list. The scan (file I/O + DLL metadata reads)
    /// runs off the UI thread — inline it would visibly freeze the window on a large
    /// Mods/ folder. Calls while a scan is running coalesce into one follow-up scan.
    /// </summary>
    public async void Refresh()
    {
        // Refresh runs from the constructor and after every operation, so it must never
        // throw (RefreshOnceAsync catches everything), and it must not overlap itself
        // (two interleaved scans would double-fill the collections).
        if (_refreshRunning)
        {
            _refreshPending = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            do
            {
                _refreshPending = false;
                await RefreshOnceAsync();
            } while (_refreshPending);
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        try
        {
            var gamePath = ModkitConfig.Current.GameInstallPath;

            // Everything filesystem-bound happens off-thread; only the collection and
            // property updates run back on the UI thread.
            var (ordered, melonInstalled, melonVersion, bundledModpackLoaderVersion) = await Task.Run(() =>
            {
                var mods = _catalog.Scan()
                    .OrderBy(m => KindRank(m.Kind))
                    .ThenBy(m => m.LoadOrder ?? int.MaxValue)
                    .ThenBy(m => m.DisplayName)
                    .ToList();

                var installed = false;
                string? version = null;
                if (!string.IsNullOrEmpty(gamePath))
                {
                    // MelonLoader lives at the game root (version.dll + MelonLoader/),
                    // not in Mods/, so query the installer rather than the scanned list.
                    var installer = new ModLoaderInstaller(gamePath);
                    installed = installer.IsMelonLoaderInstalled();
                    version = installer.GetInstalledMelonLoaderVersion();
                }

                // The version this build would install (bundled DLL); an update is
                // available when it's newer than what's in Mods/.
                var bundledLoader = ModLoaderInstaller.GetBundledModpackLoaderVersion();

                return (mods, installed, version, bundledLoader);
            });

            Mods.Clear();
            Rows.Clear();

            foreach (var mod in ordered)
                Mods.Add(mod);

            // One section per kind present, in rank order, with a header row.
            foreach (var group in ordered.GroupBy(m => m.Kind))
            {
                Rows.Add(new ModSection(SectionTitle(group.Key), group.Count()));
                foreach (var mod in group)
                    Rows.Add(mod);
            }

            var path = _catalog.ModsPath;
            Status = path == null
                ? "Game not located — click Locate game… (or set MENACE_GAME_PATH)."
                : $"{Mods.Count} mod(s) — {path}";

            RefreshLoaderStatuses(gamePath, melonInstalled, melonVersion, bundledModpackLoaderVersion);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't read the Mods folder: {ex.Message}";
            Status = "Scan failed.";
        }
    }

    // Infrastructure (loaders) first, then modpacks, Jiangyu mods, raw MelonMods, leader packs.
    private static int KindRank(ModKind kind) => kind switch
    {
        ModKind.Infrastructure => 0,
        ModKind.Modpack => 1,
        ModKind.Jiangyu => 2,
        ModKind.MelonMod => 3,
        ModKind.Leader => 4,
        _ => 5,
    };

    private static string SectionTitle(ModKind kind) => kind switch
    {
        ModKind.Infrastructure => "INFRASTRUCTURE",
        ModKind.Modpack => "MODPACKS",
        ModKind.Jiangyu => "JIANGYU MODS",
        ModKind.MelonMod => "MELONLOADER MODS",
        ModKind.Leader => "CUSTOM LEADERS",
        _ => "OTHER",
    };

    private void RefreshLoaderStatuses(
        string? gamePath, bool melonInstalled, string? melonVersion, string? bundledModpackLoaderVersion)
    {
        if (string.IsNullOrEmpty(gamePath))
        {
            MelonLoaderStatus = ModpackLoaderStatus = JiangyuLoaderStatus = "game not located";
            _melonLoaderInstalledVersion = _jiangyuInstalledVersion = null;
            ModpackLoaderUpdateAvailable = false;
        }
        else
        {
            _melonLoaderInstalledVersion = melonInstalled ? melonVersion : null;
            MelonLoaderStatus = melonInstalled
                ? $"installed{(string.IsNullOrEmpty(melonVersion) ? "" : $" {melonVersion}")}"
                : "not installed";

            // The other two appear in the scan as infrastructure DLLs in Mods/.
            var modpackLoaderInstalled = Mods
                .FirstOrDefault(m => string.Equals(m.Id, "Menace.ModpackLoader.dll", StringComparison.OrdinalIgnoreCase))
                ?.Version;
            ModpackLoaderStatus = DescribeModpackLoader(modpackLoaderInstalled, bundledModpackLoaderVersion);
            ModpackLoaderUpdateAvailable =
                !string.IsNullOrEmpty(modpackLoaderInstalled) && IsNewerVersion(bundledModpackLoaderVersion, modpackLoaderInstalled);

            JiangyuLoaderStatus = DescribeInstalled("Jiangyu.Loader.dll");
            _jiangyuInstalledVersion = Mods
                .FirstOrDefault(m => string.Equals(m.Id, "Jiangyu.Loader.dll", StringComparison.OrdinalIgnoreCase))
                ?.Version;
        }

        this.RaisePropertyChanged(nameof(CanGetMelonLoader));
        this.RaisePropertyChanged(nameof(CanGetJiangyu));
    }

    /// <summary>
    /// Status line for the Modpack Loader (bundled with this build). Flags when the
    /// bundled version is newer than what's installed in the game.
    /// </summary>
    private static string DescribeModpackLoader(string? installed, string? bundled)
    {
        if (string.IsNullOrEmpty(installed))
            return "not installed";
        if (IsNewerVersion(bundled, installed))
            return $"installed {installed} — update to {bundled} available";
        return $"installed {installed}";
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is a strictly newer release than
    /// <paramref name="current"/>. Tolerates a leading "v" and git-describe suffixes
    /// ("1.2.3-8-g…"); returns false if either can't be parsed (never a false "update").
    /// </summary>
    private static bool IsNewerVersion(string? candidate, string? current)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(current))
            return false;

        static Version? Parse(string raw)
        {
            var core = raw.TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(core, out var v) ? v : null;
        }

        var a = Parse(candidate);
        var b = Parse(current);
        return a != null && b != null && a > b;
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
            // Fall through to Refresh: the checkbox flipped visually on click, so the
            // list must be re-read to show the on-disk truth again.
        }

        Refresh();
    }

    /// <summary>
    /// The single install entry (button + drag-drop). Handles a mod archive, a folder, or a
    /// bare .dll, and works out for itself whether the mod needs compiling: a modpack with C#
    /// sources is compiled and deployed; everything else is copied straight in.
    /// </summary>
    public Task InstallAsync(string path) => InstallManyAsync(new[] { path });

    /// <summary>
    /// Install a batch (multi-file drop/paste) as ONE operation: per-file failures are
    /// collected and surfaced together at the end. Running them as separate operations
    /// meant each cleared <see cref="ErrorMessage"/> on start, so a failure on file 1
    /// silently vanished when file 2 succeeded.
    /// </summary>
    public Task InstallManyAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return Task.CompletedTask;

        // Progress must be created on the UI thread so its callbacks marshal Status back.
        var progress = new Progress<string>(s => Status = s);
        var busyStatus = paths.Count == 1
            ? $"Installing {System.IO.Path.GetFileName(paths[0])}…"
            : $"Installing {paths.Count} mods…";

        return ExecuteAsync(busyStatus, async () =>
        {
            var failures = new List<string>();
            foreach (var path in paths)
            {
                try
                {
                    await Task.Run(() => InstallCoreAsync(path, progress));
                }
                catch (Exception ex)
                {
                    failures.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");
                }
            }

            if (failures.Count == 1 && paths.Count == 1)
                throw new InvalidOperationException(failures[0]);
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    $"{failures.Count} of {paths.Count} installs failed:\n" + string.Join("\n", failures));
        });
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
            await _deployService.DeployAsync(modDir, progress, deployedBy: "standalone");
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

/// <summary>A section header row in the mod list (one per <see cref="Menace.Modkit.ModManagement.ModKind"/> present).</summary>
public sealed record ModSection(string Title, int Count);
