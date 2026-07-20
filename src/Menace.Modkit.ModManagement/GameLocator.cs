using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Locates the MENACE install by walking every Steam library on the machine: probe the
/// known Steam roots per platform, parse each root's <c>steamapps/libraryfolders.vdf</c>
/// (which lists every library, including ones on other drives), and look for the game in
/// each library's <c>steamapps/common/</c>.
/// </summary>
public static class GameLocator
{
    private static readonly string[] GameFolderNames = { "Menace", "Menace Demo" };

    /// <summary>Find the game folder, or null. Never throws.</summary>
    public static string? FindGame()
    {
        try
        {
            foreach (var steamApps in EnumerateSteamAppsDirs())
            foreach (var name in GameFolderNames)
            {
                var candidate = Path.Combine(steamApps, "common", name);
                if (LooksLikeGameDir(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Detection is best-effort; the caller falls back to asking the user.
        }

        return null;
    }

    /// <summary>True when the folder looks like a MENACE install (not just any directory).</summary>
    public static bool LooksLikeGameDir(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return false;
        return File.Exists(Path.Combine(dir, "Menace.exe"))
            || Directory.Exists(Path.Combine(dir, "Menace_Data"));
    }

    private static IEnumerable<string> EnumerateSteamAppsDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var steamRoots = new[]
        {
            // Windows
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            // Linux (native, symlinked, Debian/Ubuntu package, Flatpak)
            Path.Combine(home, ".local/share/Steam"),
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".steam/debian-installation"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
        };

        foreach (var root in steamRoots)
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            if (seen.Add(Path.GetFullPath(steamApps)))
                yield return steamApps;

            // libraryfolders.vdf lists every additional library (other drives etc.):
            //     "path"    "D:\\SteamLibrary"
            var vdf = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            string text;
            try { text = File.ReadAllText(vdf); }
            catch { continue; }

            foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                var library = m.Groups[1].Value.Replace(@"\\", @"\");
                var libraryApps = Path.Combine(library, "steamapps");
                if (Directory.Exists(libraryApps) && seen.Add(Path.GetFullPath(libraryApps)))
                    yield return libraryApps;
            }
        }
    }
}
