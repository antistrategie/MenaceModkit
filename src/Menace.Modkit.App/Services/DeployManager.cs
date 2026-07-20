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
/// clones and loads loose assets, so no game files are ever patched. Undeploy retains a
/// legacy restore of resources.assets/globalgamemanagers from .original backups left by
/// older Modkit versions that baked into game files.
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
            // Refresh runtime DLLs from the bundled directory, then deploy them first so
            // ModpackLoader.dll is available as a compile reference.
            _modpackManager.RefreshRuntimeDlls();
            progress?.Report("Deploying runtime DLLs...");
            await Task.Run(() => DeployRuntimeDlls(modsBasePath), ct);

            // Always recompile when sources exist — the Modkit is the authoring tool and
            // the author may have just edited the code.
            await _deployService.DeployAsync(modpack.Path, progress, ct, forceCompile: true, deployedBy: DeployedByMarker);

            ModkitLog.Info($"Deployed {modpack.Name} to {modsBasePath}");
            progress?.Report($"Deployed {modpack.Name}");
            return new DeployResult { Success = true, Message = $"Deployed {modpack.Name}", DeployedCount = 1 };
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
            // Remove modkit-deployed modpacks that are no longer in staging. Ownership
            // comes from the deployedBy marker in each dir's runtime manifest (stateless);
            // the previous deploy-state record covers dirs deployed before the marker
            // existed. Mods installed by other tools or by hand are never touched.
            var currentModpackNames = new HashSet<string>(modpacks.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            var retired = FindOwnedModpackDirs(modsBasePath)
                .Concat(previousState.DeployedModpacks
                    .Where(mp => !string.IsNullOrWhiteSpace(mp.Name))
                    .Select(mp => Path.Combine(modsBasePath, mp.Name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(dir => !currentModpackNames.Contains(Path.GetFileName(dir)));
            foreach (var dir in retired)
            {
                if (Path.GetFullPath(dir) == Path.GetFullPath(modsBasePath) || !Directory.Exists(dir))
                    continue;
                ModkitLog.Info($"[DeployManager] Modpack '{Path.GetFileName(dir)}' no longer in staging, removing");
                Directory.Delete(dir, true);
            }

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
                await _deployService.DeployAsync(modpack.Path, progress, ct, forceCompile: true, deployedBy: DeployedByMarker);

                deployedModpacks.Add(new DeployedModpack
                {
                    Name = modpack.Name,
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
            return new DeployResult { Success = true, Message = $"Deployed {total} modpack(s)", DeployedCount = total };
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
            // Step 1: Pre-undeploy validation
            progress?.Report("Validating deployment state...");
            var validation = await ValidateBeforeUndeployAsync(progress);

            // Log validation results
            if (!validation.IsValid)
            {
                ModkitLog.Warn($"[DeployManager] Pre-undeploy validation found issues: {validation.Summary}");

                foreach (var modified in validation.ModifiedFiles)
                {
                    ModkitLog.Warn($"[DeployManager] Modified since deploy: {modified.RelativePath} " +
                        $"(expected {modified.ExpectedSize}B, actual {modified.ActualSize}B)");
                }

                foreach (var missing in validation.MissingFiles)
                {
                    ModkitLog.Info($"[DeployManager] Already removed: {missing}");
                }

                foreach (var unknown in validation.UnknownFiles)
                {
                    ModkitLog.Info($"[DeployManager] Unknown file (will be preserved): {unknown}");
                }
            }

            // Check for critical issues that would prevent safe undeploy
            if (!validation.ShouldProceed)
            {
                var errorMsg = $"Cannot proceed with undeploy: {validation.Summary}";
                ModkitLog.Error($"[DeployManager] {errorMsg}");
                return new DeployResult { Success = false, Message = errorMsg };
            }

            // Log warnings about backup validation
            if (validation.BackupValidation != null && !validation.BackupValidation.IsValid)
            {
                if (validation.BackupValidation.IsCritical)
                {
                    ModkitLog.Error($"[DeployManager] Critical backup issue: {validation.BackupValidation.Summary}");
                }
                else
                {
                    ModkitLog.Warn($"[DeployManager] Backup issue: {validation.BackupValidation.Summary}");
                }
            }

            // Step 2: Proceed with file removal
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

            // Restore original game data files
            progress?.Report("Restoring original game data...");
            await Task.Run(() => RestoreOriginalGameData(modsBasePath), ct);

            // Clear deploy state
            var emptyState = new DeployState();
            emptyState.SaveTo(DeployStateFilePath);

            // Force garbage collection to release any cached file handles or data
            // This fixes an issue where undeploy+redeploy without app restart would fail
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            progress?.Report("All mods undeployed");
            return new DeployResult { Success = true, Message = "All mods undeployed" };
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
    /// Validate the current deployment state before undeploy.
    /// Checks for modified files, missing files, and unknown files.
    /// </summary>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Validation result with details about any issues found</returns>
    public async Task<UndeployValidationResult> ValidateBeforeUndeployAsync(IProgress<string>? progress = null)
    {
        var result = new UndeployValidationResult { IsValid = true };
        var modsBasePath = _modpackManager.ModsBasePath;

        if (string.IsNullOrEmpty(modsBasePath))
        {
            result.IsValid = false;
            result.Summary = "Game install path not set";
            result.ShouldProceed = false;
            return result;
        }

        var state = DeployState.LoadFrom(DeployStateFilePath);

        if (state.DeployedModpacks.Count == 0 && state.DeployedFiles.Count == 0 && state.DeployedFileInfos.Count == 0)
        {
            result.IsValid = true;
            result.Summary = "No deployment to validate";
            return result;
        }

        progress?.Report("Validating deployed files...");

        await Task.Run(() =>
        {
            // Check for modified and missing files
            if (state.DeployedFileInfos.Count > 0)
            {
                var validationErrors = state.ValidateDeployedFiles(modsBasePath);

                foreach (var error in validationErrors)
                {
                    switch (error.ErrorType)
                    {
                        case FileValidationErrorType.Missing:
                            result.MissingFiles.Add(error.RelativePath);
                            break;

                        case FileValidationErrorType.SizeMismatch:
                        case FileValidationErrorType.HashMismatch:
                            result.ModifiedFiles.Add(new ModifiedFileInfo
                            {
                                RelativePath = error.RelativePath,
                                ExpectedSize = error.ExpectedSize,
                                ActualSize = error.ActualSize,
                                ExpectedHash = error.ExpectedHash,
                                ActualHash = error.ActualHash,
                                ModificationType = error.ErrorType == FileValidationErrorType.SizeMismatch
                                    ? ModificationType.SizeChanged
                                    : ModificationType.ContentChanged
                            });
                            break;
                    }
                }
            }

            // Check for orphaned/unknown files in Mods/ folder
            var excludePatterns = new[] { "Menace.*.dll", "*.pdb", "deploy-state.json" };
            var orphanedFiles = state.GetOrphanedFiles(modsBasePath, excludePatterns);

            // Separate orphaned files into modpack directories (untracked) vs root level (unknown)
            foreach (var orphaned in orphanedFiles)
            {
                var firstSegment = orphaned.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
                bool isInModpackDir = state.DeployedModpacks.Any(m =>
                    string.Equals(m.Name, firstSegment, StringComparison.OrdinalIgnoreCase));

                if (isInModpackDir)
                {
                    result.UntrackedFiles.Add(orphaned);
                }
                else
                {
                    result.UnknownFiles.Add(orphaned);
                }
            }
        });

        // Validate backup files for game restoration
        progress?.Report("Validating backup files...");
        result.BackupValidation = await ValidateBackupsAsync();

        result.BuildSummary();
        return result;
    }

    /// <summary>
    /// Validate backup files needed for game restoration.
    /// </summary>
    private async Task<BackupValidationSummary> ValidateBackupsAsync()
    {
        var summary = new BackupValidationSummary { IsValid = true };

        var gameInstallPath = _modpackManager.GetGameInstallPath();
        if (string.IsNullOrEmpty(gameInstallPath))
        {
            summary.IsValid = false;
            summary.IsCritical = true;
            summary.Summary = "Cannot locate game install path";
            return summary;
        }

        var gameDataDir = Directory.GetDirectories(gameInstallPath, "*_Data").FirstOrDefault();
        if (string.IsNullOrEmpty(gameDataDir))
        {
            summary.IsValid = false;
            summary.IsCritical = true;
            summary.Summary = "Cannot locate game data directory";
            return summary;
        }

        return await Task.Run(() =>
        {
            // Check for backup files
            var backupFiles = new[] { "resources.assets.original", "globalgamemanagers.original" };
            foreach (var backupFile in backupFiles)
            {
                var backupPath = Path.Combine(gameDataDir, backupFile);
                if (!File.Exists(backupPath))
                {
                    // Missing backup is only critical if the patched file exists
                    var originalFile = backupFile.Replace(".original", "");
                    var originalPath = Path.Combine(gameDataDir, originalFile);
                    if (File.Exists(originalPath))
                    {
                        summary.MissingBackups.Add(backupFile);
                    }
                }
            }

            // Load and validate backup metadata if available
            var metadata = BackupMetadata.LoadFrom(gameDataDir);
            summary.HasMetadata = metadata != null;

            if (metadata != null)
            {
                var validationResult = metadata.ValidateBackups(gameDataDir);
                if (!validationResult.IsValid)
                {
                    summary.IsValid = false;
                    // Include both hash mismatches AND size mismatches as corrupted
                    summary.CorruptedBackups.AddRange(validationResult.HashMismatches);
                    summary.CorruptedBackups.AddRange(validationResult.SizeMismatches.Keys);

                    // Check if corrupted files are critical - BOTH hash AND size mismatches count
                    var allCorrupted = validationResult.HashMismatches
                        .Concat(validationResult.SizeMismatches.Keys)
                        .ToList();
                    if (allCorrupted.Any(f =>
                        f.Contains("resources.assets") || f.Contains("globalgamemanagers")))
                    {
                        summary.IsCritical = true;
                    }

                    summary.Summary = validationResult.GetSummary();
                }
            }
            else if (summary.MissingBackups.Count > 0)
            {
                summary.IsValid = false;
                summary.IsCritical = true;
                summary.Summary = $"Missing backup files: {string.Join(", ", summary.MissingBackups)}. " +
                                 "Game files cannot be restored. You may need to verify game files via Steam.";
            }

            if (summary.IsValid && summary.MissingBackups.Count == 0)
            {
                summary.Summary = "All backup files are valid";
            }

            return summary;
        });
    }

    /// <summary>
    /// Clean up orphaned files in the Mods/ folder that aren't tracked in deploy state.
    /// </summary>
    /// <param name="options">Options controlling cleanup behavior</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>List of files that were removed (or would be removed in dry-run mode)</returns>
    public async Task<List<string>> CleanupOrphanedFilesAsync(
        OrphanedFileCleanupOptions options,
        IProgress<string>? progress = null)
    {
        var removedFiles = new List<string>();
        var modsBasePath = _modpackManager.ModsBasePath;

        if (string.IsNullOrEmpty(modsBasePath))
            return removedFiles;

        var state = DeployState.LoadFrom(DeployStateFilePath);

        // Always exclude core DLLs and common config files
        var excludePatterns = new List<string>(options.ProtectedPatterns)
        {
            "Menace.*.dll",
            "*.pdb",
            "deploy-state.json"
        };

        var orphanedFiles = state.GetOrphanedFiles(modsBasePath, excludePatterns);

        progress?.Report($"Found {orphanedFiles.Count} orphaned file(s)");

        await Task.Run(() =>
        {
            foreach (var relativePath in orphanedFiles)
            {
                var fullPath = Path.Combine(modsBasePath, relativePath);

                if (options.DryRun)
                {
                    ModkitLog.Info($"[DeployManager] Would remove orphaned file: {relativePath}");
                    removedFiles.Add(relativePath);
                }
                else
                {
                    try
                    {
                        File.Delete(fullPath);
                        ModkitLog.Info($"[DeployManager] Removed orphaned file: {relativePath}");
                        removedFiles.Add(relativePath);
                    }
                    catch (Exception ex)
                    {
                        ModkitLog.Warn($"[DeployManager] Failed to remove orphaned file {relativePath}: {ex.Message}");
                    }
                }
            }

            // Remove empty directories if requested
            if (options.RemoveEmptyDirectories && !options.DryRun)
            {
                RemoveEmptyDirectories(modsBasePath);
            }
        });

        return removedFiles;
    }

    /// <summary>
    /// Remove empty directories recursively.
    /// </summary>
    private static void RemoveEmptyDirectories(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return;

        foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)) // Process deepest directories first
        {
            try
            {
                if (Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    ModkitLog.Info($"[DeployManager] Removed empty directory: {Path.GetRelativePath(rootPath, dir)}");
                }
            }
            catch
            {
                // Ignore errors removing directories
            }
        }
    }

    // ---------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------

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
    /// Restore original game data files from backups.
    /// Verifies backup integrity using BackupMetadata before restoring.
    /// </summary>
    private void RestoreOriginalGameData(string modsBasePath)
    {
        ModkitLog.Info("[DeployManager] RestoreOriginalGameData starting...");

        var gameInstallPath = _modpackManager.GetGameInstallPath();
        if (string.IsNullOrEmpty(gameInstallPath))
        {
            ModkitLog.Warn("[DeployManager] RestoreOriginalGameData: gameInstallPath is empty, cannot restore");
            return;
        }

        ModkitLog.Info($"[DeployManager] RestoreOriginalGameData: gameInstallPath = {gameInstallPath}");

        var gameDataDir = Directory.GetDirectories(gameInstallPath, "*_Data").FirstOrDefault();
        if (string.IsNullOrEmpty(gameDataDir))
        {
            ModkitLog.Warn($"[DeployManager] RestoreOriginalGameData: No *_Data directory found in {gameInstallPath}");
            return;
        }

        ModkitLog.Info($"[DeployManager] RestoreOriginalGameData: gameDataDir = {gameDataDir}");

        // Load backup metadata for hash verification
        var backupMetadata = BackupMetadata.LoadFrom(gameDataDir);
        if (backupMetadata != null)
        {
            ModkitLog.Info($"[DeployManager] Found backup metadata: version={backupMetadata.GameVersion}, " +
                $"created={backupMetadata.BackupCreatedAt:yyyy-MM-dd HH:mm}, files={backupMetadata.FileHashes.Count}");
        }
        else
        {
            ModkitLog.Warn("[DeployManager] No backup metadata found - proceeding with size-based validation only");
        }

        // Files to restore
        var filesToRestore = new[] { "resources.assets", "globalgamemanagers" };

        // Expected minimum sizes for validation (vanilla game files)
        var expectedMinSizes = new Dictionary<string, long>
        {
            { "resources.assets", 500 * 1024 * 1024 }, // ~518MB for vanilla
            { "globalgamemanagers", 5 * 1024 * 1024 }  // ~6MB for vanilla
        };

        foreach (var originalName in filesToRestore)
        {
            var originalPath = Path.Combine(gameDataDir, originalName);
            var backupPath = Path.Combine(gameDataDir, originalName + ".original");

            if (File.Exists(backupPath))
            {
                try
                {
                    var backupSize = new FileInfo(backupPath).Length;

                    // Validate backup isn't corrupted (too small)
                    if (expectedMinSizes.TryGetValue(originalName, out var minSize) && backupSize < minSize)
                    {
                        ModkitLog.Error($"[DeployManager] Backup {originalName}.original appears corrupted: {backupSize / 1024 / 1024}MB (expected >{minSize / 1024 / 1024}MB). Use Steam to verify game files, then use Clean Redeploy.");
                        continue;
                    }

                    // Verify hash against metadata if available
                    bool hashVerified = false;
                    if (backupMetadata != null && backupMetadata.FileHashes.TryGetValue(originalName, out var expectedHash))
                    {
                        ModkitLog.Info($"[DeployManager] Verifying backup hash for {originalName}...");
                        var actualHash = DeployState.ComputeFileHash(backupPath);
                        if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            ModkitLog.Info($"[DeployManager] Backup hash verified: {originalName} matches metadata");
                            hashVerified = true;
                        }
                        else
                        {
                            // CRITICAL: Do NOT restore corrupted backups - this could damage the game installation
                            ModkitLog.Error($"[DeployManager] Backup hash mismatch for {originalName}! " +
                                $"Expected: {expectedHash[..16]}..., Actual: {actualHash[..16]}... " +
                                "SKIPPING restore - backup may be corrupted. Use Steam to verify game files.");
                            continue;
                        }
                    }

                    ModkitLog.Info($"[DeployManager] Restoring original: {originalName}.original ({backupSize / 1024 / 1024}MB) -> {originalName}");
                    File.Copy(backupPath, originalPath, overwrite: true);

                    // Verify restored file
                    var restoredSize = new FileInfo(originalPath).Length;
                    if (restoredSize != backupSize)
                    {
                        ModkitLog.Error($"[DeployManager] Restore verification failed for {originalName}: " +
                            $"size mismatch (backup={backupSize}, restored={restoredSize})");
                    }
                    else if (hashVerified || backupMetadata == null)
                    {
                        ModkitLog.Info($"[DeployManager] Restored {originalName}: {restoredSize / 1024 / 1024}MB (verified)");
                    }
                    else
                    {
                        // Verify hash of restored file if metadata exists
                        var restoredHash = DeployState.ComputeFileHash(originalPath);
                        if (backupMetadata.FileHashes.TryGetValue(originalName, out var expectedRestoredHash) &&
                            string.Equals(restoredHash, expectedRestoredHash, StringComparison.OrdinalIgnoreCase))
                        {
                            ModkitLog.Info($"[DeployManager] Restored {originalName}: {restoredSize / 1024 / 1024}MB (hash verified after copy)");
                        }
                        else
                        {
                            ModkitLog.Info($"[DeployManager] Restored {originalName}: {restoredSize / 1024 / 1024}MB");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModkitLog.Error($"[DeployManager] Failed to restore {originalName}: {ex.Message}");
                }
            }
            else
            {
                ModkitLog.Warn($"[DeployManager] No backup found for {originalName} at {backupPath}");
            }
        }

        ModkitLog.Info("[DeployManager] RestoreOriginalGameData complete");
    }
}

public class DeployResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DeployedCount { get; set; }
}
