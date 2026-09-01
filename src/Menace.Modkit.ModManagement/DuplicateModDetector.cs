using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Two or more enabled Jiangyu mod folders declaring the same manifest name.
/// <see cref="Keep"/> is the most recently written copy; the rest are almost certainly
/// leftovers.
/// </summary>
public sealed record DuplicateModGroup(string Name, ManagedMod Keep, IReadOnlyList<ManagedMod> Stale)
{
    /// <summary>One line naming the collision and which copy the manager would drop.</summary>
    public string Describe()
    {
        var stale = string.Join(", ", Stale.Select(m => $"'{FolderName(m)}'"));
        return $"'{Name}' is installed twice — as '{FolderName(Keep)}' and {stale}. " +
               "The Jiangyu loader blocks every copy when a manifest name is duplicated, so " +
               $"none of them load. The older copy ({stale}) can be removed.";
    }

    private static string FolderName(ManagedMod mod) => new DirectoryInfo(mod.Location).Name;
}

/// <summary>
/// Finds Jiangyu mod folders that collide on their manifest name.
///
/// The usual cause is a load-order rename: the manager prefixes a folder (<c>010-Foo</c>),
/// then the mod's author redeploys, and <c>ModDeployer</c> writes <c>Mods/Foo</c> fresh
/// beside it. Both carry the same manifest name, and the loader's duplicate-name check
/// blocks the pair rather than guessing.
/// </summary>
public static class DuplicateModDetector
{
    /// <summary>
    /// Group the enabled Jiangyu folder mods that share a manifest name. Ordinal matching
    /// mirrors the loader's own grouping. Disabled mods live outside <c>Mods/</c>, so the
    /// loader never sees them and they cannot collide.
    /// </summary>
    public static IReadOnlyList<DuplicateModGroup> Find(IEnumerable<ManagedMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        return mods
            .Where(m => m.OrderedByFolderName && m.IsEnabled)
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                // Newest wins: a redeploy rewrites its folder, so the freshly written copy is
                // the one the author meant to be there.
                var ranked = group.OrderByDescending(LastWritten).ThenBy(m => m.Location, StringComparer.Ordinal).ToList();
                return new DuplicateModGroup(group.Key, ranked[0], ranked.Skip(1).ToList());
            })
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static DateTime LastWritten(ManagedMod mod)
    {
        try { return Directory.GetLastWriteTimeUtc(mod.Location); }
        catch { return DateTime.MinValue; }
    }
}
