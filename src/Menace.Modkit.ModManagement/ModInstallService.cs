using System;
using System.IO;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Installs mods into <c>Mods/</c> by copying a source folder or <c>.dll</c>, and uninstalls
/// by deleting a mod wherever it lives (<c>Mods/</c> or <c>DisabledMods/</c>). Purely
/// filesystem operations — no ledger, no game-file patching. Compilation of source-based
/// modpacks is a separate concern (the future deploy service), not part of a plain install.
/// </summary>
public sealed class ModInstallService
{
    private readonly IModkitConfig _config;

    public ModInstallService(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    public string? ModsPath =>
        string.IsNullOrEmpty(_config.GameInstallPath) ? null : Path.Combine(_config.GameInstallPath, "Mods");

    /// <summary>
    /// Install a mod from a source folder (modpack / Jiangyu / loose files) or a single
    /// <c>.dll</c> into <c>Mods/</c>. Returns the installed path. Throws if it already exists.
    /// </summary>
    public string Install(string sourcePath)
    {
        var modsPath = ModsPath ?? throw new InvalidOperationException("Game install path is not set.");
        Directory.CreateDirectory(modsPath);

        if (Directory.Exists(sourcePath))
        {
            var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var target = Path.Combine(modsPath, name);
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException($"'{name}' is already installed.");

            CopyDirectory(sourcePath, target);
            return target;
        }

        if (File.Exists(sourcePath) && sourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(modsPath, Path.GetFileName(sourcePath));
            if (File.Exists(target))
                throw new IOException($"'{Path.GetFileName(target)}' is already installed.");

            File.Copy(sourcePath, target);
            return target;
        }

        throw new NotSupportedException(
            $"Cannot install '{sourcePath}'. Expected a mod folder or a .dll file.");
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

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
