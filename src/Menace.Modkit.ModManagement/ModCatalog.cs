using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Menace.Modkit.App.Models;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Scans the game's <c>Mods/</c> directory and returns a unified list of everything
/// installed — Modkit modpacks, raw MelonLoader mods, and Jiangyu mods — classified and
/// with real metadata. Stateless: every <see cref="Scan"/> reads the filesystem afresh.
/// </summary>
public sealed class ModCatalog
{
    // Modkit's own infrastructure DLLs — shown but never managed.
    private static readonly HashSet<string> InfrastructureDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "Menace.ModpackLoader.dll",
        "Menace.DataExtractor.dll",
    };

    // Runtime/framework DLLs that are never mods.
    private static readonly string[] SystemDllPrefixes =
    {
        "System.", "Microsoft.", "Newtonsoft.", "Mono.", "Il2Cpp", "netstandard",
        "MelonLoader", "0Harmony", "Harmony",
    };

    private const string DisabledSuffix = ".disabled";

    private readonly IModkitConfig _config;

    public ModCatalog(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    /// <summary>The game's <c>Mods/</c> directory, or null if the game is not located.</summary>
    public string? ModsPath =>
        string.IsNullOrEmpty(_config.GameInstallPath)
            ? null
            : Path.Combine(_config.GameInstallPath, "Mods");

    /// <summary>
    /// Enumerate all mods currently in <c>Mods/</c>. Returns an empty list when the game
    /// is not located or has no <c>Mods/</c> directory yet.
    /// </summary>
    public IReadOnlyList<ManagedMod> Scan()
    {
        var result = new List<ManagedMod>();
        var modsPath = ModsPath;
        if (modsPath == null || !Directory.Exists(modsPath))
            return result;

        result.AddRange(ScanModFolders(modsPath));
        result.AddRange(ScanLooseDlls(modsPath));
        return result;
    }

    /// <summary>
    /// Classify each top-level folder in <c>Mods/</c>: a <c>modpack.json</c> is a Modkit
    /// modpack; a <c>jiangyu.json</c> is a Jiangyu mod (folder with code/bundles/locales).
    /// </summary>
    private static IEnumerable<ManagedMod> ScanModFolders(string modsPath)
    {
        foreach (var dir in Directory.GetDirectories(modsPath))
        {
            if (File.Exists(Path.Combine(dir, "modpack.json")))
                yield return FromModpack(dir);
            else if (File.Exists(Path.Combine(dir, "jiangyu.json")))
                yield return FromJiangyu(dir);
            // otherwise: not a recognised mod folder — skip
        }
    }

    private static ManagedMod FromModpack(string dir)
    {
        var manifest = ModpackManifest.LoadFromFile(Path.Combine(dir, "modpack.json"));
        var name = string.IsNullOrEmpty(manifest?.Name) ? Path.GetFileName(dir) : manifest!.Name;

        return new ManagedMod
        {
            Kind = ModKind.Modpack,
            Id = name,
            DisplayName = name,
            Version = manifest?.Version ?? string.Empty,
            Author = manifest?.Author ?? string.Empty,
            IsEnabled = true, // present in Mods/ == active
            Location = dir,
        };
    }

    private static ManagedMod FromJiangyu(string dir)
    {
        var name = Path.GetFileName(dir);
        var version = string.Empty;
        var author = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "jiangyu.json")));
            var root = doc.RootElement;
            name = JsonString(root, "name") ?? name;
            version = JsonString(root, "version") ?? string.Empty;
            author = JsonString(root, "author") ?? string.Empty;
        }
        catch
        {
            // Malformed jiangyu.json — still surface the mod using the folder name.
        }

        return new ManagedMod
        {
            Kind = ModKind.Jiangyu,
            Id = name,
            DisplayName = name,
            Version = version,
            Author = author,
            IsEnabled = true,
            Location = dir,
        };
    }

    /// <summary>Read a string property (case-insensitively) from a JSON object, or null.</summary>
    private static string? JsonString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.NameEquals(name) || string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
        }
        return null;
    }

    private static IEnumerable<ManagedMod> ScanLooseDlls(string modsPath)
    {
        foreach (var file in Directory.EnumerateFiles(modsPath))
        {
            var fileName = Path.GetFileName(file);

            var enabled = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var disabled = fileName.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (!enabled && !disabled)
                continue;

            // Normalise to the "foo.dll" name regardless of the .disabled suffix.
            var baseName = enabled ? fileName : fileName[..^DisabledSuffix.Length];

            if (IsSystemDll(baseName))
                continue;

            var isInfra = InfrastructureDlls.Contains(baseName);
            var info = MelonModInspector.Inspect(file);

            var kind = isInfra
                ? ModKind.Infrastructure
                : info?.IsJiangyu == true ? ModKind.Jiangyu : ModKind.MelonMod;

            var displayName = !string.IsNullOrEmpty(info?.Name)
                ? info!.Name!
                : Path.GetFileNameWithoutExtension(baseName);

            yield return new ManagedMod
            {
                Kind = kind,
                Id = baseName,
                DisplayName = displayName,
                Version = info?.Version ?? string.Empty,
                Author = info?.Author ?? string.Empty,
                IsEnabled = enabled,
                Location = file,
            };
        }
    }

    private static bool IsSystemDll(string fileName) =>
        SystemDllPrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
