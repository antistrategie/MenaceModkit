using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Menace.Modkit.App.Models;

namespace Menace.Modkit.App.Services;

/// <summary>
/// Owns the game's Mods/ folder. Deploys are loose files only (shared ModDeployService:
/// compile → copy → runtime manifest); the runtime loader applies patches, registers
/// clones and loads loose assets, so no game files are ever patched. This version never
/// writes to game files — if it detects a pre-v37 baked install (leftover .original
/// backups that differ from the live files) it warns the user to verify game files in
/// Steam rather than restoring them itself.
/// </summary>
public class DeployManager
{
    private readonly ModpackManager _modpackManager;
    private readonly Menace.Modkit.ModManagement.ModDeployService _deployService = new();

    /// <summary>
    /// Provenance value stamped into each deployed modpack's runtime manifest
    /// (<c>deployedBy</c>). Ownership is read back from the file system, so cleanup of
    /// retired modpacks is stateless and never touches mods installed by other tools.
    /// </summary>
    internal const string DeployedByMarker = "modkit";

    private string DeployStateFilePath =>
        Path.Combine(Path.GetDirectoryName(_modpackManager.StagingBasePath)!, "deploy-state.json");

    public DeployManager(ModpackManager modpackManager)
    {
        _modpackManager = modpackManager;
    }

    /// <summary>
    /// Deploy one modpack loosely into <c>Mods/</c> via the shared <see cref="Menace.Modkit.ModManagement.ModDeployService"/>
    /// (compile → copy tree → runtime manifest) — the same path the standalone manager uses.
    /// No game files are touched: the runtime loader applies patches, registers clones and
    /// loads loose assets from <c>Mods/</c> alone.
    /// </summary>
    public async Task<DeployResult> DeploySingleAsync(ModpackManifest modpack, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var modsBasePath = _modpackManager.ModsBasePath;
        if (string.IsNullOrEmpty(modsBasePath))
            return new DeployResult { Success = false, Message = "Game install path not set" };

        try
        {
            // Warn (read-only) if this install still has baked game files from a pre-v37
            // Modkit — we never overwrite game files, so tell the user to verify in Steam.
            var legacyWarning = await Task.Run(DetectLegacyBake, ct);
            if (legacyWarning != null)
                progress?.Report(legacyWarning);

            // Refresh runtime DLLs from the bundled directory, then deploy them first so
            // ModpackLoader.dll is available as a compile reference.
            _modpackManager.RefreshRuntimeDlls();
            progress?.Report("Deploying runtime DLLs...");
            await Task.Run(() => DeployRuntimeDlls(modsBasePath), ct);

            // Always recompile when sources exist — the Modkit is the authoring tool and
            // the author may have just edited the code.
            var target = await _deployService.DeployAsync(modpack.Path, progress, ct, forceCompile: true, deployedBy: DeployedByMarker);

            // Keep the (informational) deploy state in step so HasChangedSinceDeploy
            // doesn't report drift after every single deploy.
            UpsertDeployState(modpack, Path.GetFileName(target));

            ModkitLog.Info($"Deployed {modpack.Name} to {modsBasePath}");
            progress?.Report($"Deployed {modpack.Name}");
            return new DeployResult
            {
                Success = true,
                Message = legacyWarning == null ? $"Deployed {modpack.Name}" : $"Deployed {modpack.Name}. ⚠ {legacyWarning}",
                Warning = legacyWarning,
                DeployedCount = 1,
            };
        }
        catch (OperationCanceledException)
        {
            return new DeployResult { Success = false, Message = "Deployment cancelled" };
        }
        catch (Exception ex)
        {
            ModkitLog.Error($"Deploy single failed: {ex}");
            return new DeployResult { Success = false, Message = $"Deploy failed: {ex.Message}" };
        }
    }
    /// <summary>
    /// Deploy all active staging modpacks loosely into <c>Mods/</c> (shared ModDeployService
    /// per modpack, load order preserved), and remove previously-deployed modpacks that are
    /// no longer in staging. <c>deploy-state.json</c> is kept as an informational record for
    /// undeploy and install-health checks; the game's own files are never modified.
    /// </summary>
    public async Task<DeployResult> DeployAllAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var modsBasePath = _modpackManager.ModsBasePath;
        if (string.IsNullOrEmpty(modsBasePath))
            return new DeployResult { Success = false, Message = "Game install path not set" };

        // Staging modpacks in load order, dev-only excluded unless enabled, first
        // occurrence wins on duplicate names.
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modpacks = _modpackManager.GetStagingModpacks()
            .Where(m => !IsDevOnlyModpack(m.Name) || AppSettings.Instance.EnableDeveloperTools)
            .OrderBy(m => m.LoadOrder)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Where(m => seenNames.Add(m.Name))
            .ToList();

        if (modpacks.Count == 0)
            return new DeployResult { Success = false, Message = "No staging modpacks found" };

        var previousState = DeployState.LoadFrom(DeployStateFilePath);

        try
        {
            // Warn (read-only) if this install still has baked game files from a pre-v37
            // Modkit — we never overwrite game files, so tell the user to verify in Steam.
            var legacyWarning = await Task.Run(DetectLegacyBake, ct);
            if (legacyWarning != null)
                progress?.Report(legacyWarning);

            // Remove modkit-deployed modpacks that are no longer in staging. Ownership
            // comes from the deployedBy marker in each dir's runtime manifest (stateless);
            // the previous deploy-state record covers dirs deployed before the marker
            // existed. Mods installed by other tools or by hand are never touched.
            // Names are matched on the folder DeployAsync will actually produce (the
            // sanitised manifest name) — matching on the raw manifest name would retire
            // and redeploy any modpack whose name needed sanitising, every cycle.
            var currentModpackNames = new HashSet<string>(
                modpacks.Select(m => Menace.Modkit.ModManagement.ModDeployService.DeployFolderNameFor(m.Path, m.Name)),
                StringComparer.OrdinalIgnoreCase);
            var retired = FindOwnedModpackDirs(modsBasePath)
                .Concat(previousState.DeployedModpacks
                    .Select(mp => mp.DeployedDirName ?? mp.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => Path.Combine(modsBasePath, n)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(dir => !currentModpackNames.Contains(Path.GetFileName(dir)));
            foreach (var dir in retired)
            {
                if (Path.GetFullPath(dir) == Path.GetFullPath(modsBasePath) || !Directory.Exists(dir))
                    continue;
                ModkitLog.Info($"[DeployManager] Modpack '{Path.GetFileName(dir)}' no longer in staging, removing");
                Directory.Delete(dir, true);
            }

            // Retire split-out leader packs whose owning modpack is gone (marker-owned only).
            RetireOwnedLeaderPacks(modsBasePath, owner => owner != null && !currentModpackNames.Contains(owner));

            _modpackManager.RefreshRuntimeDlls();
            progress?.Report("Deploying runtime DLLs...");
            var runtimeFiles = await Task.Run(() => DeployRuntimeDlls(modsBasePath), ct);

            var deployedModpacks = new List<DeployedModpack>();
            int total = modpacks.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var modpack = modpacks[i];
                progress?.Report($"Deploying {modpack.Name} ({i + 1}/{total})...");
                var target = await _deployService.DeployAsync(modpack.Path, progress, ct, forceCompile: true, deployedBy: DeployedByMarker);

                deployedModpacks.Add(new DeployedModpack
                {
                    Name = modpack.Name,
                    DeployedDirName = Path.GetFileName(target),
                    Version = modpack.Version,
                    LoadOrder = modpack.LoadOrder,
                    ContentHash = ComputeDirectoryHash(modpack.Path),
                    SecurityStatus = modpack.SecurityStatus
                });
            }

            var state = new DeployState
            {
                DeployedModpacks = deployedModpacks,
                DeployedFiles = runtimeFiles,
                LastDeployTimestamp = DateTime.Now
            };
            state.SaveTo(DeployStateFilePath);

            progress?.Report($"Deployed {total} modpack(s) successfully");
            return new DeployResult
            {
                Success = true,
                Message = legacyWarning == null ? $"Deployed {total} modpack(s)" : $"Deployed {total} modpack(s). ⚠ {legacyWarning}",
                Warning = legacyWarning,
                DeployedCount = total,
            };
        }
        catch (OperationCanceledException)
        {
            return new DeployResult { Success = false, Message = "Deployment cancelled" };
        }
        catch (Exception ex)
        {
            ModkitLog.Error($"[DeployManager] Deploy failed: {ex}");
            return new DeployResult { Success = false, Message = $"Deploy failed: {ex.Message}" };
        }
    }
    /// <summary>
    /// Remove all deployed mods from the game's Mods/ folder.
    /// Core infrastructure DLLs (ModpackLoader, DataExtractor) are preserved.
    /// Performs pre-undeploy validation and logs any unexpected changes.
    /// </summary>
    public async Task<DeployResult> UndeployAllAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var modsBasePath = _modpackManager.ModsBasePath;
        if (string.IsNullOrEmpty(modsBasePath))
            return new DeployResult { Success = false, Message = "Game install path not set" };

        var state = DeployState.LoadFrom(DeployStateFilePath);

        try
        {
            // Undeploy is stateless: remove marker-owned modpack dirs (+ legacy
            // deploy-state records) and restore any legacy baked game files. There is
            // no longer per-file tracking to validate against.
            progress?.Report("Removing deployed mods...");

            await Task.Run(() =>
            {
                // Remove modkit-deployed modpack directories: everything carrying our
                // deployedBy marker (stateless scan), plus anything the deploy-state
                // record still tracks from before the marker existed.
                var toRemove = FindOwnedModpackDirs(modsBasePath)
                    .Concat(state.DeployedModpacks
                        .Where(mp => !string.IsNullOrWhiteSpace(mp.Name))
                        .Select(mp => Path.Combine(modsBasePath, mp.Name)))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var dir in toRemove)
                {
                    // Safety: never delete the Mods folder itself
                    if (Path.GetFullPath(dir) == Path.GetFullPath(modsBasePath))
                    {
                        ModkitLog.Warn($"[DeployManager] Skipping deletion of Mods folder itself");
                        continue;
                    }

                    if (Directory.Exists(dir))
                    {
                        ModkitLog.Info($"[DeployManager] Removing modpack directory: {Path.GetFileName(dir)}");
                        Directory.Delete(dir, true);
                    }
                }

                // Also remove any tracked loose files, but protect core DLLs
                foreach (var file in state.DeployedFiles)
                {
                    var fileName = Path.GetFileName(file);

                    // Never remove core infrastructure DLLs (Menace.*.dll)
                    if (fileName.StartsWith("Menace.", StringComparison.OrdinalIgnoreCase) &&
                        fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        ModkitLog.Info($"[DeployManager] Protected from removal: {file}");
                        continue;
                    }

                    var fullPath = Path.Combine(modsBasePath, file);
                    if (File.Exists(fullPath))
                    {
                        ModkitLog.Info($"[DeployManager] Removing: {file}");
                        File.Delete(fullPath);
                    }
                }

                // Remove split-out leader packs we deployed (marker-owned; user-installed
                // packs carry no marker and are never touched).
                RetireOwnedLeaderPacks(modsBasePath, _ => true);

                // Clean up deployment artifacts that shouldn't persist
                var artifactDirs = new[] { "compiled", "dll", "dlls" };
                foreach (var artifactName in artifactDirs)
                {
                    var artifactDir = Path.Combine(modsBasePath, artifactName);
                    if (Directory.Exists(artifactDir))
                    {
                        ModkitLog.Info($"[DeployManager] Removing artifact directory: {artifactName}");
                        Directory.Delete(artifactDir, true);
                    }
                }

                // Log preserved core DLLs
                foreach (var dllPath in Directory.GetFiles(modsBasePath, "Menace.*.dll"))
                {
                    ModkitLog.Info($"[DeployManager] Core DLL preserved: {Path.GetFileName(dllPath)}");
                }
            }, ct);

            // v37 never patches game files, so there is nothing to restore. If this install
            // still carries baked files from an old Modkit, point the user at Steam's verify.
            var legacyWarning = await Task.Run(DetectLegacyBake, ct);
            if (legacyWarning != null)
                progress?.Report(legacyWarning);

            // Clear deploy state
            var emptyState = new DeployState();
            emptyState.SaveTo(DeployStateFilePath);

            // Force garbage collection to release any cached file handles or data
            // This fixes an issue where undeploy+redeploy without app restart would fail
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            progress?.Report("All mods undeployed");
            return new DeployResult
            {
                Success = true,
                Message = legacyWarning == null ? "All mods undeployed" : $"All mods undeployed. ⚠ {legacyWarning}",
                Warning = legacyWarning,
            };
        }
        catch (Exception ex)
        {
            return new DeployResult { Success = false, Message = $"Undeploy failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Get the current deploy state (what's deployed vs what's in staging).
    /// </summary>
    public DeployState GetDeployState()
    {
        return DeployState.LoadFrom(DeployStateFilePath);
    }

    /// <summary>
    /// Check if any staging modpack has changed since last deploy.
    /// </summary>
    public bool HasChangedSinceDeploy()
    {
        var state = GetDeployState();
        var staging = _modpackManager.GetStagingModpacks();

        // Different count
        if (state.DeployedModpacks.Count != staging.Count)
            return true;

        // Check each modpack for changes
        foreach (var deployed in state.DeployedModpacks)
        {
            var stagingMatch = staging.FirstOrDefault(s => s.Name == deployed.Name);
            if (stagingMatch == null)
                return true; // modpack removed from staging

            var currentHash = ComputeDirectoryHash(stagingMatch.Path);
            if (currentHash != deployed.ContentHash)
                return true; // content changed
        }

        // Check for new staging modpacks not yet deployed
        foreach (var s in staging)
        {
            if (!state.DeployedModpacks.Any(d => d.Name == s.Name))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Copy runtime DLLs from runtime/ into the game install.
    /// Menace.* mod DLLs go to Mods/, support libraries go to UserLibs/.
    /// Note: Core DLLs are NOT tracked in deploy state - they are infrastructure
    /// that should persist across undeploy/deploy cycles.
    /// </summary>
    private List<string> DeployRuntimeDlls(string modsBasePath)
    {
        // We intentionally return an empty list - core DLLs should not be tracked
        // in deploy state since they're infrastructure, not user content.
        // UndeployAll should not remove them.
        var runtimeDlls = _modpackManager.GetRuntimeDlls();

        ModkitLog.Info($"[DeployManager] DeployRuntimeDlls: Found {runtimeDlls.Count} runtime DLLs to deploy");

        if (runtimeDlls.Count == 0)
        {
            ModkitLog.Warn($"[DeployManager] DeployRuntimeDlls: No runtime DLLs found in {_modpackManager.RuntimeDllsPath}");
            return new List<string>();
        }

        var gameInstallPath = Path.GetDirectoryName(modsBasePath) ?? modsBasePath;
        var userLibsPath = Path.Combine(gameInstallPath, "UserLibs");
        Directory.CreateDirectory(userLibsPath);

        foreach (var (fileName, sourcePath) in runtimeDlls)
        {
            var isModDll = fileName.StartsWith("Menace.", StringComparison.OrdinalIgnoreCase);
            var destPath = Path.Combine(isModDll ? modsBasePath : userLibsPath, fileName);
            try
            {
                // Copy if destination doesn't exist or source is different size/newer
                bool needsCopy = !File.Exists(destPath);
                if (!needsCopy)
                {
                    var srcInfo = new FileInfo(sourcePath);
                    var destInfo = new FileInfo(destPath);
                    needsCopy = srcInfo.Length != destInfo.Length ||
                                srcInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc;
                }

                if (needsCopy)
                {
                    File.Copy(sourcePath, destPath, true);
                    ModkitLog.Info($"[DeployManager] Deployed runtime DLL: {fileName} -> {(isModDll ? "Mods" : "UserLibs")}");
                }

                // Remove legacy support-library copies from Mods/ to avoid duplicate load contexts.
                if (!isModDll)
                {
                    var legacyModsPath = Path.Combine(modsBasePath, fileName);
                    if (File.Exists(legacyModsPath))
                    {
                        File.Delete(legacyModsPath);
                        ModkitLog.Info($"[DeployManager] Removed legacy dependency from Mods: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModkitLog.Warn($"[DeployManager] Failed to deploy {fileName}: {ex.Message}");
            }
        }

        // Return empty list - core DLLs are not tracked for undeploy
        return new List<string>();
    }

    private static string ComputeDirectoryHash(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        using var sha = SHA256.Create();
        var sb = new StringBuilder();

        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f))
        {
            var relativePath = Path.GetRelativePath(directory, file);
            sb.Append(relativePath);
            sb.Append(new FileInfo(file).LastWriteTimeUtc.Ticks);
            sb.Append(new FileInfo(file).Length);
        }

        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Directories to exclude when copying modpacks to Mods/ folder.
    /// These are development artifacts that shouldn't be deployed.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "src",      // Source code (compiled to build/)
        "build",    // Build output (DLLs deployed separately via DeployDlls)
        "obj",      // MSBuild intermediate files
        "bin",      // MSBuild output files
        "dll",      // Legacy DLL folder (use dlls/ instead)
        ".git",     // Git repository data
        ".vs",      // Visual Studio data
    };

    /// <summary>
    /// Scan <c>Mods/</c> for modpack directories whose runtime manifest carries our
    /// <see cref="DeployedByMarker"/> — the stateless record of what the modkit deployed.
    /// Unreadable manifests are treated as not ours (never delete what we can't identify).
    /// </summary>
    private static List<string> FindOwnedModpackDirs(string modsBasePath)
    {
        var owned = new List<string>();
        if (!Directory.Exists(modsBasePath))
            return owned;

        foreach (var dir in Directory.GetDirectories(modsBasePath))
        {
            var manifestPath = Path.Combine(dir, "modpack.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("deployedBy", out var by) &&
                    by.ValueKind == JsonValueKind.String &&
                    string.Equals(by.GetString(), DeployedByMarker, StringComparison.OrdinalIgnoreCase))
                {
                    owned.Add(dir);
                }
            }
            catch
            {
                // Malformed manifest — not provably ours, leave it alone.
            }
        }

        return owned;
    }

    /// <summary>
    /// Check if a modpack is a developer-only modpack that should be excluded
    /// from deployment unless EnableDeveloperTools is enabled.
    /// </summary>
    private static bool IsDevOnlyModpack(string modpackName)
    {
        // Modpacks starting with "Test" are developer tools
        if (modpackName.StartsWith("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Update (or insert) one modpack's entry in the informational deploy state after a
    /// single deploy, so change tracking doesn't drift until the next Deploy All.
    /// </summary>
    private void UpsertDeployState(ModpackManifest modpack, string deployedDirName)
    {
        try
        {
            var state = DeployState.LoadFrom(DeployStateFilePath);
            state.DeployedModpacks.RemoveAll(mp =>
                string.Equals(mp.Name, modpack.Name, StringComparison.OrdinalIgnoreCase));
            state.DeployedModpacks.Add(new DeployedModpack
            {
                Name = modpack.Name,
                DeployedDirName = deployedDirName,
                Version = modpack.Version,
                LoadOrder = modpack.LoadOrder,
                ContentHash = ComputeDirectoryHash(modpack.Path),
                SecurityStatus = modpack.SecurityStatus
            });
            state.LastDeployTimestamp = DateTime.Now;
            state.SaveTo(DeployStateFilePath);
        }
        catch (Exception ex)
        {
            // Informational only — a failed write must never fail the deploy.
            ModkitLog.Warn($"[DeployManager] Couldn't update deploy state: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove split-out CustomLeader packs this Modkit deployed. Ownership is the
    /// provenance marker <see cref="Menace.Modkit.ModManagement.ModDeployService"/> stamps
    /// on each split-out pack; packs without a marker (installed by the user, the
    /// standalone manager, or by hand) are never touched.
    /// </summary>
    private static void RetireOwnedLeaderPacks(string modsBasePath, Func<string?, bool> ownerRetired)
    {
        var root = Path.Combine(modsBasePath, "customleaders");
        if (!Directory.Exists(root))
            return;

        foreach (var pack in Directory.GetDirectories(root))
        {
            var marker = Menace.Modkit.ModManagement.ModDeployService.ReadLeaderPackMarker(pack);
            if (marker?.DeployedBy != DeployedByMarker || !ownerRetired(marker.Owner))
                continue;

            ModkitLog.Info($"[DeployManager] Removing leader pack '{Path.GetFileName(pack)}' (split out of '{marker.Owner ?? "unknown"}')");
            try { Directory.Delete(pack, true); }
            catch (Exception ex)
            {
                ModkitLog.Warn($"[DeployManager] Failed to remove leader pack '{pack}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Read-only check for a pre-v37 <em>baked</em> install: older Modkit versions wrote
    /// patched <c>resources.assets</c>/<c>globalgamemanagers</c> over the game's own files.
    /// v37 deploys are loose files only and never touch game data, so an upgraded install
    /// would silently keep that stale baked content underneath the runtime patches. We do
    /// NOT try to restore it (that would overwrite game files); instead we detect the state
    /// — <c>.original</c> backups exist and the live file differs from its backup — and
    /// return a warning telling the user to let Steam restore vanilla. Returns null when
    /// nothing is baked (the common case). Self-clearing: once the user verifies files, the
    /// live file matches vanilla again and the warning stops. Never throws.
    /// </summary>
    private string? DetectLegacyBake()
    {
        try
        {
            var gameInstallPath = _modpackManager.GetGameInstallPath();
            if (string.IsNullOrEmpty(gameInstallPath))
                return null;

            var gameDataDir = Directory.GetDirectories(gameInstallPath, "*_Data").FirstOrDefault();
            if (string.IsNullOrEmpty(gameDataDir))
                return null;

            foreach (var name in new[] { "resources.assets", "globalgamemanagers" })
            {
                var currentPath = Path.Combine(gameDataDir, name);
                var backupPath = currentPath + ".original";
                if (!File.Exists(backupPath) || !File.Exists(currentPath))
                    continue;

                // Cheap length check first; equal lengths get a hash compare.
                if (new FileInfo(currentPath).Length != new FileInfo(backupPath).Length
                    || !string.Equals(
                        DeployState.ComputeFileHash(currentPath),
                        DeployState.ComputeFileHash(backupPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    ModkitLog.Warn("[DeployManager] Legacy baked game files detected (pre-v37 install)");
                    return "This install has modified game files from an older Modkit version. " +
                        "Verify game files in Steam (right-click MENACE → Properties → Installed Files → " +
                        "Verify integrity) before playing, or those old baked mods will stack under the new loader.";
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            ModkitLog.Warn($"[DeployManager] Legacy bake check failed: {ex.Message}");
            return null;
        }
    }
}

public class DeployResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// A non-fatal advisory shown to the user even on success (e.g. a pre-v37 baked
    /// install that should be verified in Steam). Null when there's nothing to flag.
    /// </summary>
    public string? Warning { get; set; }

    public int DeployedCount { get; set; }
}
