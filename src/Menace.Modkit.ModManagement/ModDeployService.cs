using System;
using System.IO;
using System.Linq;
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

        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        // 3. Copy the mod tree, minus source/build artefacts.
        progress?.Report($"Deploying {name}…");
        CopyTree(sourceDir, target, isRoot: true);

        // 4. Assemble dlls/ from the compiled build output + any prebuilt DLLs.
        AssembleDlls(sourceDir, manifest, target);

        // 5. Write the runtime modpack.json (stats/*.json + clones/*.json merged in).
        RuntimeManifestWriter.Write(sourceDir, target, deployedBy);

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
        return manifest.Code.PrebuiltDlls.Any(rel => File.Exists(Path.Combine(dir, rel)));
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

        // Prebuilt DLLs declared in the manifest.
        foreach (var prebuilt in manifest.Code.PrebuiltDlls)
        {
            var full = Path.Combine(sourceDir, prebuilt);
            if (File.Exists(full))
            {
                Directory.CreateDirectory(dllDir);
                File.Copy(full, Path.Combine(dllDir, Path.GetFileName(full)), overwrite: true);
            }
        }
    }

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

    /// <summary>True if the two paths are the same, or one contains the other.</summary>
    private static bool PathsOverlap(string a, string b)
    {
        var fa = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fa.StartsWith(fb, StringComparison.Ordinal) || fb.StartsWith(fa, StringComparison.Ordinal);
    }
}
