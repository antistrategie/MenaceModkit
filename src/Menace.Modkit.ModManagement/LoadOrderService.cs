using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Reads and writes the <c>loadOrder</c> field of a deployed modpack's <c>modpack.json</c>.
///
/// The edit is deliberately surgical — parse, set one key, write back — rather than a
/// round-trip through <c>ModpackManifest</c>. The deployed manifest carries keys the typed
/// model has no property for (<c>clones</c>, the legacy <c>templates</c> block,
/// <c>deployedBy</c>), and serialising the model back over it would silently drop them.
/// Unknown keys written by a future loader survive for the same reason.
///
/// State stays on disk: <c>Mods/</c> is the single source of truth, so a reorder is just a
/// set of manifest writes that the next scan reads back.
/// </summary>
public sealed class LoadOrderService
{
    /// <summary>
    /// Spacing between assigned orders. Wide enough that a user can hand-place a pack
    /// between two others without renumbering (the loader treats it as a plain int).
    /// </summary>
    public const int Step = 10;

    private readonly IModkitConfig _config;

    public LoadOrderService(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    /// <summary>
    /// Write <paramref name="order"/> as a mod's load order: a modpack's manifest
    /// <c>loadOrder</c>, or the numeric prefix on a Jiangyu mod's folder name. No-op when
    /// the mod already holds that value.
    /// </summary>
    /// <returns>True if something on disk changed.</returns>
    public bool SetLoadOrder(ManagedMod mod, int order)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return mod.Kind switch
        {
            ModKind.Modpack => SetModpackLoadOrder(mod, order),
            ModKind.Jiangyu => SetJiangyuLoadOrder(mod, order),
            _ => throw new InvalidOperationException(
                $"'{mod.DisplayName}' has no load order. Modpacks carry one in modpack.json and " +
                "Jiangyu mods carry one in their folder name; MelonLoader mods are ordered by the " +
                "[MelonPriority] compiled into them."),
        };
    }

    /// <summary>
    /// Rename a Jiangyu mod's folder so it carries <paramref name="order"/> as its prefix.
    /// The loader walks <c>Mods/</c> in ordinal folder order, and a mod's identity is its
    /// manifest <c>name</c>, so the folder name is the ordering knob.
    /// </summary>
    private bool SetJiangyuLoadOrder(ManagedMod mod, int order)
    {
        var location = ValidatedLocation(mod);
        var parent = Path.GetDirectoryName(location)
            ?? throw new InvalidOperationException($"'{mod.DisplayName}' has no parent directory.");

        var currentName = new DirectoryInfo(location).Name;
        var targetName = JiangyuFolderOrder.Compose(order, JiangyuFolderOrder.StripPrefix(currentName));
        if (string.Equals(currentName, targetName, StringComparison.Ordinal))
            return false;

        var targetPath = Path.Combine(parent, targetName);
        if (Directory.Exists(targetPath))
            throw new IOException(
                $"Can't reorder '{mod.DisplayName}': '{targetName}' already exists.");

        Directory.Move(location, targetPath);
        return true;
    }

    private bool SetModpackLoadOrder(ManagedMod mod, int order)
    {
        var manifestPath = ManifestPathFor(mod);
        var json = File.ReadAllText(manifestPath);

        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(json)?.AsObject()
                  ?? throw new InvalidDataException("manifest is not a JSON object");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"'{mod.DisplayName}' has a malformed modpack.json and can't be reordered: {ex.Message}", ex);
        }

        // Case-insensitive, because a hand-written manifest may use "LoadOrder".
        var existingKey = obj.Select(kvp => kvp.Key)
            .FirstOrDefault(k => string.Equals(k, "loadOrder", StringComparison.OrdinalIgnoreCase));

        if (existingKey != null && (int?)obj[existingKey] == order)
            return false;

        obj.Remove(existingKey ?? "loadOrder");
        obj["loadOrder"] = order;

        WriteAtomic(manifestPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    /// <summary>
    /// Renumber a whole list of modpacks to <see cref="Step"/>-spaced orders matching their
    /// position in <paramref name="modpacksInOrder"/> (first loads first; last applies last
    /// and so wins field conflicts). Only manifests whose value actually changes are written.
    ///
    /// Relative order is preserved exactly, so an author's deliberate spread (10 vs 9999)
    /// is normalised but never inverted.
    /// </summary>
    /// <returns>The names of the modpacks whose manifests were rewritten.</returns>
    public IReadOnlyList<string> ApplyOrdering(IReadOnlyList<ManagedMod> modpacksInOrder)
    {
        ArgumentNullException.ThrowIfNull(modpacksInOrder);

        var written = new List<string>();
        var failures = new List<string>();

        // A Jiangyu mod carries its order in a fixed-width folder prefix, which tops out at
        // JiangyuFolderOrder.MaxOrder. Step spacing collapses to 1 for a list long enough to
        // run past that, so ordinal folder order still matches the numbers.
        var step = modpacksInOrder.Any(m => m.OrderedByFolderName)
                   && modpacksInOrder.Count * Step > JiangyuFolderOrder.MaxOrder
            ? 1
            : Step;

        for (var i = 0; i < modpacksInOrder.Count; i++)
        {
            var mod = modpacksInOrder[i];
            try
            {
                if (SetLoadOrder(mod, (i + 1) * step))
                    written.Add(mod.DisplayName);
            }
            catch (Exception ex)
            {
                // One unwritable manifest must not abandon the rest half-renumbered: the
                // others still land, and the next scan shows whatever is actually on disk.
                failures.Add($"{mod.DisplayName}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
            throw new AggregateLoadOrderException(written, failures);

        return written;
    }

    /// <summary>
    /// The mod's on-disk folder, confirmed to exist and to sit inside the game's own mod
    /// directories. A Location pointing anywhere else means the scan root moved under us
    /// (or a crafted mod name), and neither a manifest write nor a rename may follow it.
    /// </summary>
    private string ValidatedLocation(ManagedMod mod)
    {
        if (string.IsNullOrEmpty(mod.Location) || !Directory.Exists(mod.Location))
            throw new DirectoryNotFoundException($"'{mod.DisplayName}' no longer exists at '{mod.Location}'.");

        var gamePath = _config.GameInstallPath;
        if (string.IsNullOrEmpty(gamePath))
            throw new InvalidOperationException("Game install path is not set.");

        var roots = new[] { Path.Combine(gamePath, "Mods"), Path.Combine(gamePath, "DisabledMods") };
        if (!roots.Any(r => IsUnder(mod.Location, r)))
            throw new InvalidOperationException(
                $"Refusing to write outside the game's mod folders: '{mod.Location}'.");

        return mod.Location;
    }

    private string ManifestPathFor(ManagedMod mod)
    {
        var manifestPath = Path.Combine(ValidatedLocation(mod), "modpack.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"'{mod.DisplayName}' has no modpack.json.", manifestPath);

        return manifestPath;
    }

    /// <summary>
    /// Write via a temp file in the same directory then replace, so a crash mid-write can't
    /// leave a truncated manifest — which the loader would skip, silently dropping the mod.
    /// </summary>
    private static void WriteAtomic(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
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

/// <summary>
/// Thrown when a reorder partially applied: <see cref="Written"/> landed on disk,
/// <see cref="Failures"/> did not.
/// </summary>
public sealed class AggregateLoadOrderException : Exception
{
    public AggregateLoadOrderException(IReadOnlyList<string> written, IReadOnlyList<string> failures)
        : base(string.Join("\n", failures))
    {
        Written = written;
        Failures = failures;
    }

    public IReadOnlyList<string> Written { get; }
    public IReadOnlyList<string> Failures { get; }
}
