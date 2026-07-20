using System;
using System.IO;
using System.Text.Json;
using Menace.Modkit.ModManagement;

namespace Menace.ModManager;

/// <summary>
/// <see cref="IModkitConfig"/> for the standalone Mod Manager. Locates the game via, in
/// order: the <c>MENACE_GAME_PATH</c> environment variable, the user's persisted choice,
/// then <see cref="GameLocator"/> (walks every Steam library via libraryfolders.vdf).
///
/// Only the app's own settings (the chosen path) are persisted — mod <em>state</em> is
/// never stored; that always comes from scanning <c>Mods/</c>.
/// </summary>
public sealed class StandaloneModkitConfig : IModkitConfig
{
    public string? GameInstallPath { get; private set; } = DetectGamePath();

    public bool EnableDeveloperTools => false;
    public string? ComponentsCachePath => null;
    public string UpdateChannel => "stable";
    public bool IsBetaChannel => false;
    public bool HasUsedModdingTools => false;
    public string AppVersionFull => "MenaceModManager";
    public string LoaderVersion => "0.0.0";

    /// <summary>Set (and persist) a user-chosen game folder. Validates the shape first.</summary>
    public void SetGamePath(string path)
    {
        if (!GameLocator.LooksLikeGameDir(path))
            throw new ArgumentException(
                "That folder doesn't look like a MENACE install (expected Menace.exe or Menace_Data/).");

        GameInstallPath = path;
        SaveSettings(path);
    }

    private static string? DetectGamePath()
    {
        var env = Environment.GetEnvironmentVariable("MENACE_GAME_PATH");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        var saved = LoadSavedPath();
        if (GameLocator.LooksLikeGameDir(saved))
            return saved;

        return GameLocator.FindGame();
    }

    // ---- settings persistence (app settings only, never mod state) ----

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MenaceModManager", "settings.json");

    private static string? LoadSavedPath()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("gamePath", out var p) ? p.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveSettings(string gamePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(new { gamePath }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-fatal: the path still applies for this session.
        }
    }
}
