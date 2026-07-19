using System;
using System.IO;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Enables/disables mods by moving them between <c>Mods/</c> and a <c>DisabledMods/</c>
/// sibling directory. A sibling move (rather than an in-place rename) is required because
/// the loaders discover mods by scanning <c>Mods/</c> recursively — a renamed folder still
/// gets loaded. State stays entirely on disk: a mod's location is its enabled/disabled truth.
/// </summary>
public sealed class ModEnableService
{
    private readonly IModkitConfig _config;

    public ModEnableService(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    private string? GamePath =>
        string.IsNullOrEmpty(_config.GameInstallPath) ? null : _config.GameInstallPath;

    public string? ModsPath => GamePath is null ? null : Path.Combine(GamePath, "Mods");
    public string? DisabledPath => GamePath is null ? null : Path.Combine(GamePath, "DisabledMods");

    /// <summary>Toggle a mod on or off. No-op if it is already in the requested state.</summary>
    public void SetEnabled(ManagedMod mod, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (!mod.CanToggle)
            throw new InvalidOperationException($"'{mod.DisplayName}' is protected and cannot be toggled.");

        if (mod.IsEnabled == enabled)
            return;

        var modsPath = ModsPath;
        var disabledPath = DisabledPath;
        if (modsPath is null || disabledPath is null)
            throw new InvalidOperationException("Game install path is not set.");

        var source = mod.Location;
        if (!Exists(source))
            throw new FileNotFoundException($"Mod no longer exists at '{source}'.");

        var name = Path.GetFileName(source);

        string target;
        if (enabled)
        {
            Directory.CreateDirectory(modsPath);
            // A ".dll.disabled" parked in Mods/ is re-enabled by stripping the suffix in place.
            target = name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase) && IsUnder(source, modsPath)
                ? Path.Combine(modsPath, name[..^".disabled".Length])
                : Path.Combine(modsPath, name);
        }
        else
        {
            Directory.CreateDirectory(disabledPath);
            target = Path.Combine(disabledPath, name);
        }

        if (Exists(target))
            throw new IOException($"Target already exists: '{target}'.");

        Move(source, target);
    }

    public void Enable(ManagedMod mod) => SetEnabled(mod, true);
    public void Disable(ManagedMod mod) => SetEnabled(mod, false);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Move(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }

    private static bool IsUnder(string path, string dir)
    {
        var full = Path.GetFullPath(path);
        var baseDir = Path.GetFullPath(dir);
        if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
            baseDir += Path.DirectorySeparatorChar;
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
    }
}
