using System;
using System.IO;
using System.Linq;
using Menace.Modkit.App.Services; // PathValidator (kept its original namespace when extracted)
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Installs mods into <c>Mods/</c> from an archive (.zip/.7z/.rar/.tar...), a folder, or a
/// single <c>.dll</c>, and uninstalls by deleting a mod wherever it lives (<c>Mods/</c> or
/// <c>DisabledMods/</c>). Purely filesystem operations — no ledger, no game-file patching.
/// Compilation of source-based modpacks is a separate concern (the future deploy service).
/// </summary>
public sealed class ModInstallService
{
    private static readonly string[] ArchiveExtensions =
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2" };

    private readonly IModkitConfig _config;

    public ModInstallService(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    public string? ModsPath =>
        string.IsNullOrEmpty(_config.GameInstallPath) ? null : Path.Combine(_config.GameInstallPath, "Mods");

    /// <summary>
    /// Install a mod from an archive, a source folder, or a single <c>.dll</c> into
    /// <c>Mods/</c>. Returns the installed path. Throws if it already exists.
    /// </summary>
    public string Install(string sourcePath)
    {
        var modsPath = ModsPath ?? throw new InvalidOperationException("Game install path is not set.");
        Directory.CreateDirectory(modsPath);

        if (Directory.Exists(sourcePath))
            return InstallFolder(sourcePath, modsPath);

        if (File.Exists(sourcePath))
        {
            if (IsArchive(sourcePath))
                return InstallArchive(sourcePath, modsPath);
            if (sourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return InstallDll(sourcePath, modsPath);
        }

        throw new NotSupportedException(
            $"Cannot install '{sourcePath}'. Expected a mod archive (.zip/.7z/...), a mod folder, or a .dll.");
    }

    /// <summary>Delete a mod from disk. Refuses protected (infrastructure) mods.</summary>
    public void Uninstall(ManagedMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (mod.IsProtected)
            throw new InvalidOperationException($"'{mod.DisplayName}' is protected and cannot be uninstalled.");

        if (Directory.Exists(mod.Location))
            Directory.Delete(mod.Location, recursive: true);
        else if (File.Exists(mod.Location))
            File.Delete(mod.Location);
    }

    // ---- install kinds ----

    private static string InstallFolder(string sourceDir, string modsPath)
    {
        var name = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var target = RequireFreeTarget(modsPath, name);
        CopyDirectory(sourceDir, target);
        return target;
    }

    private static string InstallDll(string dllPath, string modsPath)
    {
        var target = RequireFreeTarget(modsPath, Path.GetFileName(dllPath));
        File.Copy(dllPath, target);
        return target;
    }

    /// <summary>
    /// Extract an archive to a temp dir, work out what the mod is (a modpack.json/jiangyu.json
    /// folder, a wrapper folder, or a bare .dll), then place it into <c>Mods/</c>.
    /// </summary>
    private static string InstallArchive(string archivePath, string modsPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "mm-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractArchive(archivePath, temp);

            var rootFiles = Directory.GetFiles(temp);
            var rootDirs = Directory.GetDirectories(temp);

            // 1) Manifest at the archive root → install the whole extracted tree as a folder.
            if (HasModManifest(temp))
                return PlaceDirectory(temp, Path.Combine(modsPath, ArchiveBaseName(archivePath)));

            // 2) A single wrapper folder (the common "name/…" publish layout).
            if (rootDirs.Length == 1 && rootFiles.Length == 0)
                return PlaceDirectory(rootDirs[0], Path.Combine(modsPath, Path.GetFileName(rootDirs[0])));

            // 3) A bare .dll (a raw MelonMod zipped up) → install it loose.
            if (rootDirs.Length == 0 && rootFiles.Length == 1 &&
                rootFiles[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var target = RequireFreeTarget(modsPath, Path.GetFileName(rootFiles[0]));
                File.Copy(rootFiles[0], target);
                return target;
            }

            // 4) Fallback: install the whole extracted tree as a folder named after the archive.
            return PlaceDirectory(temp, Path.Combine(modsPath, ArchiveBaseName(archivePath)));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    // ---- helpers ----

    private static bool IsArchive(string path) =>
        ArchiveExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static bool HasModManifest(string dir) =>
        File.Exists(Path.Combine(dir, "modpack.json")) || File.Exists(Path.Combine(dir, "jiangyu.json"));

    private static string ArchiveBaseName(string archivePath)
    {
        var name = Path.GetFileNameWithoutExtension(archivePath);
        // Handle ".tar.gz" etc.
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    private static string RequireFreeTarget(string modsPath, string name)
    {
        var target = Path.Combine(modsPath, name);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"'{name}' is already installed.");
        return target;
    }

    private static string PlaceDirectory(string sourceDir, string target)
    {
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"'{Path.GetFileName(target)}' is already installed.");
        // Copy (not move) so it works across volumes and leaves the temp dir for cleanup.
        CopyDirectory(sourceDir, target);
        return target;
    }

    private static void ExtractArchive(string archivePath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;

            // Guard against Zip Slip.
            var destPath = PathValidator.ValidateArchiveEntryPath(destinationDir, entry.Key);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            entry.WriteToFile(destPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
