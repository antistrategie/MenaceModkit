using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Launches MENACE. A Steam install goes through Steam (<c>steam://rungameid/…</c>) so
/// Proton, the user's launch options, and the overlay all apply — launching the exe
/// directly would bypass all of those and, on Linux, can't work at all (it's a Windows
/// build run under Proton). Only a non-Steam install on Windows runs Menace.exe directly.
/// </summary>
public static class GameLauncher
{
    /// <summary>MENACE's Steam app id.</summary>
    public const string SteamAppId = "2432860";

    /// <summary>
    /// Launch the game installed at <paramref name="gameInstallPath"/>. Fire-and-forget:
    /// returns once the launch has been handed to Steam/the OS. Throws with a
    /// user-presentable message when the hand-off itself fails.
    /// </summary>
    public static void Launch(string gameInstallPath)
    {
        if (LaunchesViaSteam(gameInstallPath))
            LaunchViaSteam();
        else
            LaunchExeDirectly(gameInstallPath);
    }

    /// <summary>
    /// True when <see cref="Launch"/> would go through Steam for this install rather than
    /// run Menace.exe directly. Exposed so the UI can label the launch action honestly.
    /// </summary>
    public static bool LaunchesViaSteam(string gameInstallPath)
        => !OperatingSystem.IsWindows() || IsSteamInstall(gameInstallPath);

    /// <summary>True when the install lives inside a Steam library (…/steamapps/common/…).</summary>
    public static bool IsSteamInstall(string gameInstallPath)
        // Both separators literally, not Path.*SeparatorChar — on Linux those are both
        // '/' and a Windows-style path would never split.
        => gameInstallPath
            .Split('/', '\\')
            .Any(segment => segment.Equals("steamapps", StringComparison.OrdinalIgnoreCase));

    private static void LaunchViaSteam()
    {
        var url = $"steam://rungameid/{SteamAppId}";

        if (OperatingSystem.IsWindows())
        {
            // The shell resolves the steam:// protocol handler.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        try
        {
            // Hands the URL to the running Steam client (or starts it).
            Process.Start(new ProcessStartInfo("steam", url));
        }
        catch (Win32Exception)
        {
            // No `steam` on PATH (e.g. Flatpak) — let the desktop URL handler route it.
            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(opener, url));
        }
    }

    private static void LaunchExeDirectly(string gameInstallPath)
    {
        var gameExe = Path.Combine(gameInstallPath, "Menace.exe");
        if (!File.Exists(gameExe))
            throw new FileNotFoundException($"Menace.exe not found in {gameInstallPath}.", gameExe);

        Process.Start(new ProcessStartInfo(gameExe)
        {
            WorkingDirectory = gameInstallPath,
            UseShellExecute = true,
        });
    }
}
