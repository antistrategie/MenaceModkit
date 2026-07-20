using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menace.Modkit.App.Models;   // ModpackManifest, CompilationResult (kept App namespace when extracted)
using Menace.Modkit.App.Services; // CompilationService (kept App namespace when extracted)

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Deploys a <em>source</em> modpack into <c>Mods/</c>: compiles its C# (if any), copies the
/// mod tree in (excluding source/build artefacts), assembles the compiled/prebuilt DLLs into
/// <c>dlls/</c>, and writes the runtime <c>modpack.json</c> (stats/clones merged).
///
/// Deliberately stateless and loose-file: no <c>resources.assets</c> patching, no asset-bundle
/// baking, no deploy-state ledger. The runtime loader applies patches/clones from
/// <c>modpack.json</c> and loads loose assets — so a plain file drop is all that's needed.
/// </summary>
public sealed class ModDeployService
{
    private static readonly string[] ExcludedDirs = { "src", "build", "obj", "bin", ".git", ".vs" };

    private readonly IModkitConfig _config;
    private readonly CompilationService _compiler = new();

    public ModDeployService(IModkitConfig? config = null) => _config = config ?? ModkitConfig.Current;

    public string? ModsPath =>
        string.IsNullOrEmpty(_config.GameInstallPath) ? null : Path.Combine(_config.GameInstallPath, "Mods");

    /// <summary>
    /// Compile (if needed) and deploy a source modpack folder into <c>Mods/</c>, replacing any
    /// existing install of the same name. Returns the deployed path. Throws on compile failure.
    /// <paramref name="forceCompile"/> recompiles even when a built DLL already exists — for
    /// authoring tools where the sources are the truth; installers of distributed mods leave
    /// it off so a shipped DLL is never rebuilt against missing references.
    /// </summary>
    public async Task<string> DeployAsync(string sourceDir, IProgress<string>? progress = null, CancellationToken ct = default, bool forceCompile = false, string? deployedBy = null)
    {
        var modsPath = ModsPath ?? throw new InvalidOperationException("Game install path is not set.");
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source modpack not found: {sourceDir}");

        var manifestPath = Path.Combine(sourceDir, "modpack.json");
        var manifest = File.Exists(manifestPath) ? ModpackManifest.LoadFromFile(manifestPath) : null;
        if (manifest == null)
            throw new InvalidOperationException("No modpack.json found in the source folder.");
        manifest.Path = sourceDir;

        var folderName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        // Never let a manifest-supplied name escape Mods/ (it gets Directory.Delete'd below).
        var name = SanitiseModName(manifest.Name) ?? folderName;

        // 1. Compile the mod's C# source, but only if it has sources AND isn't already built
        //    (a distributed modpack may ship its compiled DLL, in which case a copy is enough
        //    and a recompile would just risk failing on missing references).
        if (manifest.Code.HasAnySources && (forceCompile || !HasBuiltOutput(sourceDir, manifest)))
        {
            progress?.Report($"Compiling {name}…");
            var result = await _compiler.CompileModpackAsync(manifest, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                throw new InvalidOperationException($"Compile failed for {name}:\n{errors}");
            }
        }

        // 2. Fresh target — deploy replaces any existing install.
        var target = Path.Combine(modsPath, name);

        // Guard against deploying a folder that IS (or contains/is contained by) the target,
        // which would Directory.Delete the source and lose it.
        if (PathsOverlap(sourceDir, target))
            throw new InvalidOperationException(
                "Deploy the source modpack from a folder outside the game's Mods/ directory.");

        // 3-5. Build the complete deployed tree in a same-volume staging dir, then swap it
        //      in — deleting the old install before a copy that can fail midway would
        //      leave neither the old nor a complete new install.
        progress?.Report($"Deploying {name}…");
        var staging = StagingArea.Create(_config);
        try
        {
            // Copy the mod tree minus source/build artefacts, assemble dlls/ from the
            // compiled build output + prebuilt DLLs, and write the runtime modpack.json
            // (stats/*.json + clones/*.json merged in).
            CopyTree(sourceDir, staging, isRoot: true);
            AssembleDlls(sourceDir, manifest, staging);
            RuntimeManifestWriter.Write(sourceDir, staging, deployedBy, progress);

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(staging, target);
        }
        finally
        {
            StagingArea.Discard(staging);
        }

        // 6. Hybrid mods (e.g. SkyBot-style leaders) bundle CustomLeader packs beside their
        //    modpack content. The MenaceCustomLeader framework only reads the fixed
        //    Mods/customleaders/ root, so split those packs out of the modpack folder.
        SplitOutLeaderPacks(sourceDir, target, modsPath, deployedBy, name, progress);

        progress?.Report($"Deployed {name}");
        return target;
    }

    /// <summary>True if the modpack already carries a compiled DLL (dlls/, build/, or prebuilt).</summary>
    private static bool HasBuiltOutput(string dir, ModpackManifest manifest)
    {
        foreach (var sub in new[] { "dlls", "build" })
        {
            var p = Path.Combine(dir, sub);
            if (Directory.Exists(p) && Directory.EnumerateFiles(p, "*.dll").Any())
                return true;
        }
        return manifest.Code.PrebuiltDlls.Any(rel =>
            File.Exists(Path.Combine(dir, rel.Replace('\\', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// Move any bundled CustomLeader packs (<c>customleaders/&lt;name&gt;/</c> in the source)
    /// into the shared <c>Mods/customleaders/</c> root and drop them from the deployed
    /// modpack folder — the framework never looks inside modpacks. Each split-out pack is
    /// stamped with a provenance marker (deployer + owning modpack) so retire/undeploy can
    /// remove exactly these packs; a same-named pack the user installed themselves (no
    /// marker) is never overwritten.
    /// </summary>
    private static void SplitOutLeaderPacks(
        string sourceDir, string target, string modsPath,
        string? deployedBy, string owner, IProgress<string>? progress)
    {
        var bundled = Path.Combine(sourceDir, "customleaders");
        if (Directory.Exists(bundled))
        {
            foreach (var pack in Directory.GetDirectories(bundled))
            {
                var packName = Path.GetFileName(pack);
                var packTarget = Path.Combine(modsPath, "customleaders", packName);
                if (Directory.Exists(packTarget) && ReadLeaderPackMarker(packTarget) == null)
                {
                    progress?.Report(
                        $"Leader pack '{packName}' was installed separately — leaving it untouched.");
                    continue;
                }

                if (Directory.Exists(packTarget))
                    Directory.Delete(packTarget, recursive: true);
                CopyTree(pack, packTarget, isRoot: true);
                WriteLeaderPackMarker(packTarget, deployedBy, owner);
            }
        }

        var nested = Path.Combine(target, "customleaders");
        if (Directory.Exists(nested))
            Directory.Delete(nested, recursive: true);
    }

    // ---- split-out leader pack provenance ----

    private const string LeaderPackMarkerFile = ".deployedBy.json";

    /// <summary>Provenance of a split-out leader pack (see <see cref="ReadLeaderPackMarker"/>).</summary>
    public sealed record LeaderPackProvenance(string? DeployedBy, string? Owner);

    /// <summary>
    /// Read the provenance marker of a <c>Mods/customleaders/&lt;pack&gt;</c> directory.
    /// Null means the pack has no marker — installed by the user or another tool — and
    /// must never be overwritten or cleaned up by a deployer.
    /// </summary>
    public static LeaderPackProvenance? ReadLeaderPackMarker(string packDir)
    {
        try
        {
            var path = Path.Combine(packDir, LeaderPackMarkerFile);
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? Get(string name) =>
                doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
            return new LeaderPackProvenance(Get("deployedBy"), Get("owner"));
        }
        catch
        {
            // Unreadable marker → treat as not ours; never risk deleting a user's pack.
            return null;
        }
    }

    private static void WriteLeaderPackMarker(string packDir, string? deployedBy, string owner)
    {
        File.WriteAllText(
            Path.Combine(packDir, LeaderPackMarkerFile),
            JsonSerializer.Serialize(
                new { deployedBy, owner },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyTree(string source, string dest, bool isRoot)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);
            // Only strip src/build/obj/bin/… at the modpack root — a mod may legitimately
            // have e.g. an assets/obj/ folder deeper in the tree.
            if (isRoot && ExcludedDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                continue;
            CopyTree(dir, Path.Combine(dest, dirName), isRoot: false);
        }
    }

    private static void AssembleDlls(string sourceDir, ModpackManifest manifest, string target)
    {
        var dllDir = Path.Combine(target, "dlls");

        // Compiled output from build/.
        var buildDir = Path.Combine(sourceDir, "build");
        if (Directory.Exists(buildDir))
        {
            foreach (var dll in Directory.GetFiles(buildDir, "*.dll"))
            {
                Directory.CreateDirectory(dllDir);
                File.Copy(dll, Path.Combine(dllDir, Path.GetFileName(dll)), overwrite: true);
            }
        }

        // Prebuilt DLLs declared in the manifest (separators normalised for Windows-authored paths).
        foreach (var prebuilt in manifest.Code.PrebuiltDlls)
        {
            var full = Path.Combine(sourceDir, prebuilt.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                Directory.CreateDirectory(dllDir);
                File.Copy(full, Path.Combine(dllDir, Path.GetFileName(full)), overwrite: true);
            }
        }
    }

    /// <summary>
    /// The <c>Mods/</c> folder name a deploy of <paramref name="sourceDir"/> will produce
    /// — exposed so deployers can match on-disk dirs against staging without re-deriving
    /// the sanitisation (a name with invalid chars deploys under a different folder than
    /// the manifest says).
    /// </summary>
    public static string DeployFolderNameFor(string sourceDir, string? manifestName) =>
        SanitiseModName(manifestName)
            ?? Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Reduce a manifest-supplied name to a safe single folder name (no path components,
    /// no invalid chars). Returns null if nothing usable remains, so the caller falls back
    /// to the source folder name.
    /// </summary>
    private static string? SanitiseModName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var name = Path.GetFileName(raw.Trim()); // drops any directory / traversal components
        if (string.IsNullOrEmpty(name) || name == "." || name == "..")
            return null;

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// True if the two paths are the same, or one contains the other. Ignore-case:
    /// Windows/macOS resolve differently-cased paths to the same folder, and a false
    /// negative here lets the deploy delete its own source.
    /// </summary>
    private static bool PathsOverlap(string a, string b)
    {
        var fa = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fa.StartsWith(fb, StringComparison.OrdinalIgnoreCase) || fb.StartsWith(fa, StringComparison.OrdinalIgnoreCase);
    }
}
