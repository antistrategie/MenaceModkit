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

    // Author project/build artefacts that sometimes get zipped alongside a mod. These must
    // not be deployed — MelonLoader scans Mods/ recursively, so stray bin/obj DLLs would be
    // loaded as duplicate mods. Stripped at the mod-folder root only.
    private static readonly string[] ExcludedDirs = { "src", "build", "obj", "bin", ".git", ".vs" };

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
        return PlaceNamed(sourceDir, modsPath, name);
    }

    private static string InstallDll(string dllPath, string modsPath)
    {
        var target = PrepareTarget(modsPath, Path.GetFileName(dllPath));
        File.Copy(dllPath, target, overwrite: true);
        return target;
    }

    private static string InstallArchive(string archivePath, string modsPath)
    {
        using var extracted = ExtractArchiveToTemp(archivePath);
        if (extracted.BareDll != null)
            return InstallDll(extracted.BareDll, modsPath);
        return PlaceNamed(extracted.ModRoot!, modsPath, extracted.Name);
    }

    /// <summary>
    /// Copy an already-resolved mod folder into <c>Mods/&lt;name&gt;</c> (used by the caller after
    /// inspecting extracted archive contents). Strips source/build artefacts.
    /// </summary>
    public string InstallFrom(string modRootDir, string name)
    {
        var modsPath = ModsPath ?? throw new InvalidOperationException("Game install path is not set.");
        Directory.CreateDirectory(modsPath);

        // A folder is only a loadable mod when a loader reads it: modpack.json
        // (ModpackLoader) or jiangyu.json (Jiangyu).
        if (HasModManifest(modRootDir))
            return PlaceNamed(modRootDir, modsPath, name);

        // A CustomLeader leader pack ({leader}_clone.json / _replace.json + portrait art)
        // is read by the MenaceCustomLeader framework from Mods/customleaders/<name>/.
        if (IsLeaderPack(modRootDir))
            return PlaceNamed(modRootDir, Path.Combine(modsPath, "customleaders"), name);

        string target = string.Empty;

        // Bundled leader packs (the CustomLeader framework zip ships customleaders/<x>/
        // beside its DLL): merge each pack into the shared Mods/customleaders/ root.
        var bundledLeaders = Path.Combine(modRootDir, "customleaders");
        var hasBundledLeaders = Directory.Exists(bundledLeaders);
        if (hasBundledLeaders)
        {
            foreach (var pack in Directory.GetDirectories(bundledLeaders))
                target = PlaceNamed(pack, Path.Combine(modsPath, "customleaders"), Path.GetFileName(pack));
        }

        // Raw MelonMods load ONLY as top-level Mods/*.dll — MelonLoader does not scan
        // subfolders — so hoist their DLLs to the root instead of burying them in a
        // folder nothing reads.
        var dlls = Directory.GetFiles(modRootDir, "*.dll");
        foreach (var dll in dlls)
            target = InstallDll(dll, modsPath);

        // Nothing recognised: install as a plain folder (the catalog will surface it).
        if (target.Length == 0)
            return PlaceNamed(modRootDir, modsPath, name);

        // Keep any non-DLL payload (rare: a melon that reads its own data folder) in a
        // named folder beside the hoisted pieces; junk dirs, docs, consumed leader packs
        // and authoring tools/ aren't worth one.
        if (HasPayloadBeyondKnown(modRootDir, ignoreLeaderExtras: hasBundledLeaders))
        {
            var folder = PlaceNamed(modRootDir, modsPath, name);
            foreach (var placedDll in Directory.GetFiles(folder, "*.dll"))
                File.Delete(placedDll);
            var nested = Path.Combine(folder, "customleaders");
            if (Directory.Exists(nested))
                Directory.Delete(nested, recursive: true);
        }

        return target;
    }

    private static readonly string[] DocExtensions = { ".txt", ".md", ".pdf", ".rtf", ".nfo" };

    private static bool IsLeaderPack(string dir) =>
        Directory.EnumerateFiles(dir, "*_clone.json").Any() ||
        Directory.EnumerateFiles(dir, "*_replace.json").Any();

    private static bool HasPayloadBeyondKnown(string dir, bool ignoreLeaderExtras)
    {
        // Junk dirs (src/build/…) are stripped on copy anyway, so they aren't payload;
        // a consumed customleaders/ dir (and its authoring tools/) isn't either.
        if (Directory.GetDirectories(dir).Any(d =>
        {
            var n = Path.GetFileName(d);
            if (ExcludedDirs.Contains(n, StringComparer.OrdinalIgnoreCase))
                return false;
            if (ignoreLeaderExtras &&
                (n.Equals("customleaders", StringComparison.OrdinalIgnoreCase) ||
                 n.Equals("tools", StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }))
            return true;

        return Directory.GetFiles(dir).Any(f =>
            !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            !DocExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Extract an archive to a temp directory and resolve what the mod is: a folder to install
    /// (<see cref="ExtractedArchive.ModRoot"/>) or a single loose DLL
    /// (<see cref="ExtractedArchive.BareDll"/>). The caller disposes the result to clean up.
    /// </summary>
    public static ExtractedArchive ExtractArchiveToTemp(string archivePath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "mm-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractArchive(archivePath, temp);

            var rootFiles = Directory.GetFiles(temp);
            var rootDirs = Directory.GetDirectories(temp);

            // A single wrapper folder (the common "name/…" layout) — its name is authoritative.
            if (rootDirs.Length == 1 && rootFiles.Length == 0 && !HasModManifest(temp))
                return new ExtractedArchive(temp, rootDirs[0], null, Path.GetFileName(rootDirs[0]));

            // A bare .dll — a raw MelonMod zipped up.
            if (rootDirs.Length == 0 && rootFiles.Length == 1 &&
                rootFiles[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return new ExtractedArchive(temp, null, rootFiles[0], Path.GetFileName(rootFiles[0]));

            // Manifest at the root, or anything else → the whole tree, named after the archive.
            return new ExtractedArchive(temp, temp, null, ArchiveBaseName(archivePath));
        }
        catch
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            throw;
        }
    }

    // ---- helpers ----

    public static bool IsArchive(string path) =>
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

    // Compute the install target, clearing any existing install of the same name so a
    // re-install acts as an update (a clean replace, no stale files left behind).
    private static string PrepareTarget(string modsPath, string name)
    {
        var target = Path.Combine(modsPath, name);
        ClearTarget(target);
        return target;
    }

    private static void ClearTarget(string target)
    {
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        else if (File.Exists(target))
            File.Delete(target);
    }

    /// <summary>Copy a mod folder into <c>Mods/&lt;name&gt;</c> (clean replace, junk stripped, guarded).</summary>
    private static string PlaceNamed(string sourceDir, string modsPath, string name)
    {
        var target = Path.Combine(modsPath, name);

        // Copying a folder that IS (or contains) the target would delete the source.
        var src = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var tgt = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (src.StartsWith(tgt, StringComparison.Ordinal) || tgt.StartsWith(src, StringComparison.Ordinal))
            throw new InvalidOperationException("Install the mod from a location outside the game's Mods/ directory.");

        ClearTarget(target);
        // Copy (not move) so it works across volumes and leaves any temp dir for cleanup.
        CopyDirectory(sourceDir, target, isRoot: true);
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

    private static void CopyDirectory(string source, string dest, bool isRoot)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);
            if (isRoot && ExcludedDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(dest, dirName), isRoot: false);
        }
    }
}

/// <summary>
/// A mod archive extracted to a temp directory. Either <see cref="ModRoot"/> (a folder to
/// install) or <see cref="BareDll"/> (a single loose DLL) is set. Dispose to delete the temp.
/// </summary>
public sealed class ExtractedArchive : IDisposable
{
    internal ExtractedArchive(string tempDir, string? modRoot, string? bareDll, string name)
    {
        TempDir = tempDir;
        ModRoot = modRoot;
        BareDll = bareDll;
        Name = name;
    }

    public string TempDir { get; }
    public string? ModRoot { get; }
    public string? BareDll { get; }

    /// <summary>Suggested install folder name (wrapper folder name, or the archive base name).</summary>
    public string Name { get; }

    public void Dispose()
    {
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
