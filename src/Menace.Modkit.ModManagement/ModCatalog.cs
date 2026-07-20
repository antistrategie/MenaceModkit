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
    // Loader/infrastructure DLLs — shown but never toggled or uninstalled by the manager.
    private static readonly HashSet<string> InfrastructureDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "Menace.ModpackLoader.dll",
        "Menace.DataExtractor.dll",
        "Jiangyu.Loader.dll",
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

    /// <summary>Where disabled mods are parked (a sibling of <c>Mods/</c>).</summary>
    public string? DisabledPath =>
        string.IsNullOrEmpty(_config.GameInstallPath)
            ? null
            : Path.Combine(_config.GameInstallPath, "DisabledMods");

    /// <summary>
    /// Enumerate all mods — enabled ones in <c>Mods/</c> and disabled ones parked in
    /// <c>DisabledMods/</c>. Returns empty when the game is not located.
    /// </summary>
    public IReadOnlyList<ManagedMod> Scan()
    {
        var result = new List<ManagedMod>();

        var modsPath = ModsPath;
        if (modsPath != null && Directory.Exists(modsPath))
        {
            result.AddRange(ScanModFolders(modsPath, enabled: true));
            result.AddRange(ScanLooseDlls(modsPath, enabled: true));
            result.AddRange(ScanLeaderPacks(modsPath, enabled: true));
        }

        var disabledPath = DisabledPath;
        if (disabledPath != null && Directory.Exists(disabledPath))
        {
            result.AddRange(ScanModFolders(disabledPath, enabled: false));
            result.AddRange(ScanLooseDlls(disabledPath, enabled: false));
            result.AddRange(ScanLeaderPacks(disabledPath, enabled: false));
        }

        return result;
    }

    /// <summary>
    /// Classify each top-level folder in a scan root: a <c>modpack.json</c> is a Modkit
    /// modpack; a <c>jiangyu.json</c> is a Jiangyu mod (folder with code/bundles/locales).
    /// </summary>
    private static IEnumerable<ManagedMod> ScanModFolders(string root, bool enabled)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            // The CustomLeader framework's content root — its children are listed
            // individually by ScanLeaderPacks, not as one folder mod.
            if (Path.GetFileName(dir).Equals("customleaders", StringComparison.OrdinalIgnoreCase))
                continue;

            ManagedMod? mod;
            try
            {
                mod = ClassifyFolder(dir, enabled);
            }
            catch
            {
                // A single broken mod folder must never fail the whole scan (which would
                // crash the app at launch). Skip it.
                mod = null;
            }

            if (mod != null)
                yield return mod;
        }
    }

    /// <summary>
    /// List CustomLeader leader packs: each child folder of <c>customleaders/</c> holding
    /// a <c>*_clone.json</c> or <c>*_replace.json</c> (read by the MenaceCustomLeader
    /// framework). DisplayName comes from the config's <c>nickname</c> when readable.
    /// </summary>
    private static IEnumerable<ManagedMod> ScanLeaderPacks(string root, bool enabled)
    {
        var leadersRoot = Path.Combine(root, "customleaders");
        if (!Directory.Exists(leadersRoot))
            yield break;

        foreach (var dir in Directory.GetDirectories(leadersRoot))
        {
            ManagedMod? mod;
            try
            {
                mod = FromLeaderPack(dir, enabled);
            }
            catch
            {
                mod = null; // never let one broken pack kill the scan
            }

            if (mod != null)
                yield return mod;
        }
    }

    private static ManagedMod? FromLeaderPack(string dir, bool enabled)
    {
        var config = Directory.EnumerateFiles(dir, "*_clone.json")
            .Concat(Directory.EnumerateFiles(dir, "*_replace.json"))
            .FirstOrDefault();
        if (config == null)
            return null;

        var folderName = Path.GetFileName(dir);
        string display = folderName;
        string author = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            if (doc.RootElement.TryGetProperty("nickname", out var nick) && nick.ValueKind == JsonValueKind.String)
                display = nick.GetString() ?? folderName;
            if (doc.RootElement.TryGetProperty("clone_from", out var from) && from.ValueKind == JsonValueKind.String)
                author = (config.EndsWith("_replace.json", StringComparison.OrdinalIgnoreCase) ? "replaces " : "clones ")
                         + from.GetString();
        }
        catch
        {
            // Unreadable config — list it by folder name so the user can still manage it.
        }

        return new ManagedMod
        {
            Kind = ModKind.Leader,
            Id = "customleaders/" + folderName,
            DisplayName = display,
            Author = author,
            IsEnabled = enabled,
            Location = dir,
        };
    }

    private static ManagedMod? ClassifyFolder(string dir, bool enabled) =>
        File.Exists(Path.Combine(dir, "modpack.json")) ? FromModpack(dir, enabled)
        : File.Exists(Path.Combine(dir, "jiangyu.json")) ? FromJiangyu(dir, enabled)
        // A folder with a DLL but no manifest is a raw MelonMod shipped with assets.
        : FromDllFolder(dir, enabled);

    /// <summary>
    /// A raw MelonMod distributed as a folder (a top-level <c>.dll</c> plus asset/config
    /// subfolders, no manifest). Returns null if the folder holds no DLL.
    /// </summary>
    private static ManagedMod? FromDllFolder(string dir, bool enabled)
    {
        var dlls = Directory.GetFiles(dir, "*.dll");
        if (dlls.Length == 0)
        {
            // Some mods ship the DLL in a subfolder (e.g. MyMod/bin/mod.dll). Look deeper,
            // but only accept it as a mod if a nested DLL is actually a MelonMod — otherwise
            // an asset-only folder that happens to contain a stray DLL would be misclassified.
            dlls = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Where(d => MelonModInspector.Inspect(d)?.IsMelonMod == true)
                .ToArray();
            if (dlls.Length == 0)
                return null;
        }

        // Prefer the DLL carrying [MelonInfo] (the mod itself, not a bundled dependency),
        // then one named like the folder, else the first.
        MelonModInfo? info = null;
        var isJiangyu = false;
        foreach (var dll in dlls)
        {
            var inspected = MelonModInspector.Inspect(dll);
            if (inspected?.IsJiangyu == true)
                isJiangyu = true;
            if (info == null && inspected?.HasMelonInfo == true)
                info = inspected;
        }
        info ??= MelonModInspector.Inspect(
            dlls.FirstOrDefault(d => string.Equals(
                Path.GetFileNameWithoutExtension(d), Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase))
            ?? dlls[0]);

        var folderName = Path.GetFileName(dir);
        return new ManagedMod
        {
            Kind = isJiangyu || info?.IsJiangyu == true ? ModKind.Jiangyu : ModKind.MelonMod,
            Id = folderName,
            DisplayName = !string.IsNullOrEmpty(info?.Name) ? info!.Name! : folderName,
            Version = info?.Version ?? string.Empty,
            Author = info?.Author ?? string.Empty,
            IsEnabled = enabled,
            Location = dir,
        };
    }

    private static ManagedMod FromModpack(string dir, bool enabled)
    {
        ModpackManifest? manifest = null;
        try
        {
            manifest = ModpackManifest.LoadFromFile(Path.Combine(dir, "modpack.json"));
        }
        catch
        {
            // Malformed/locked modpack.json — still surface the mod using the folder name.
        }
        var name = string.IsNullOrEmpty(manifest?.Name) ? Path.GetFileName(dir) : manifest!.Name;

        return new ManagedMod
        {
            Kind = ModKind.Modpack,
            Id = name,
            DisplayName = name,
            Version = manifest?.Version ?? string.Empty,
            Author = manifest?.Author ?? string.Empty,
            IsEnabled = enabled,
            Location = dir,
        };
    }

    private static ManagedMod FromJiangyu(string dir, bool enabled)
    {
        var name = Path.GetFileName(dir);
        var version = string.Empty;
        var author = string.Empty;
        string? compiledForJiangyu = null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "jiangyu.json")));
            var root = doc.RootElement;
            name = JsonString(root, "name") ?? name;
            version = JsonString(root, "version") ?? string.Empty;
            author = JsonString(root, "author") ?? string.Empty;
            compiledForJiangyu = JsonString(root, "compiledForJiangyu");
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
            CompiledForJiangyu = compiledForJiangyu,
            IsEnabled = enabled,
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

    private static IEnumerable<ManagedMod> ScanLooseDlls(string root, bool enabled)
    {
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var fileName = Path.GetFileName(file);

            var isDll = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var isDisabledDll = fileName.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (!isDll && !isDisabledDll)
                continue;

            // Normalise to the "foo.dll" name regardless of the .disabled suffix.
            var baseName = isDll ? fileName : fileName[..^DisabledSuffix.Length];

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
                // In-place ".dll.disabled" is disabled even under Mods/; DisabledMods/ is always disabled.
                IsEnabled = enabled && isDll,
                Location = file,
            };
        }
    }

    private static bool IsSystemDll(string fileName) =>
        SystemDllPrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
