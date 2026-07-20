using System;
using System.IO;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Same-volume staging for install/deploy swaps: content is fully copied here first, then
/// renamed into place, so a mid-copy failure (locked file, disk full) never destroys an
/// existing install. Lives at <c>&lt;game&gt;/.mm-staging/</c> — inside the game folder so
/// the final <see cref="Directory.Move(string, string)"/> is a same-volume rename, but
/// outside <c>Mods/</c> so loaders and the catalog never see half-copied trees, even after
/// a crash.
/// </summary>
internal static class StagingArea
{
    private const string RootName = ".mm-staging";

    /// <summary>
    /// A fresh, empty staging dir. Sweeps leftovers from crashed runs first (installs are
    /// serialised by the calling apps, so nothing else is staging concurrently).
    /// </summary>
    public static string Create(IModkitConfig config)
    {
        var gameDir = config.GameInstallPath;
        if (string.IsNullOrEmpty(gameDir))
            throw new InvalidOperationException("Game install path is not set.");
        var root = Path.Combine(gameDir, RootName);

        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { /* a locked leftover just stays for the next sweep */ }

        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Delete a staging dir if it still exists (no-op after a successful move).</summary>
    public static void Discard(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
