using System;
using System.IO;
using System.Linq;
using Menace.Modkit.ModManagement;

namespace Menace.ModManager;

/// <summary>
/// <see cref="IModkitConfig"/> for the standalone Mod Manager. Locates the game via the
/// <c>MENACE_GAME_PATH</c> environment variable or common Steam install paths.
///
/// (A persisted settings screen will replace the probe later; mod <em>state</em> is never
/// stored here — that always comes from scanning <c>Mods/</c>.)
/// </summary>
public sealed class StandaloneModkitConfig : IModkitConfig
{
    public string? GameInstallPath { get; } = DetectGamePath();

    public bool EnableDeveloperTools => false;
    public string? ComponentsCachePath => null;
    public string UpdateChannel => "stable";
    public bool IsBetaChannel => false;
    public bool HasUsedModdingTools => false;
    public string AppVersionFull => "MenaceModManager";
    public string LoaderVersion => "0.0.0";

    private static string? DetectGamePath()
    {
        var env = Environment.GetEnvironmentVariable("MENACE_GAME_PATH");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Menace",
            @"C:\Program Files (x86)\Steam\steamapps\common\Menace Demo",
            Path.Combine(home, ".steam/steam/steamapps/common/Menace"),
            Path.Combine(home, ".steam/debian-installation/steamapps/common/Menace"),
            Path.Combine(home, ".steam/steam/steamapps/common/Menace Demo"),
            Path.Combine(home, ".steam/debian-installation/steamapps/common/Menace Demo"),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
