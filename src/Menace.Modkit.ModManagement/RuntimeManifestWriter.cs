using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Menace.Modkit.App.Models;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Writes the runtime <c>modpack.json</c> the loader consumes, merging authoring-time
/// <c>stats/*.json</c> and <c>clones/*.json</c> into the manifest's <c>patches</c>/<c>clones</c>
/// and scanning <c>assets/</c>. Shared by <see cref="ModpackManager"/> (legacy deploy) and
/// <see cref="ModDeployService"/> (stateless deploy).
/// </summary>
internal static class RuntimeManifestWriter
{
    public static void Write(
        string sourceDir, string deployDir, string? deployedBy = null, IProgress<string>? progress = null)
    {
        var manifest = LoadManifest(sourceDir);

        var runtimeObj = new JsonObject
        {
            ["manifestVersion"] = 2,
            ["name"] = manifest?.Name ?? Path.GetFileName(sourceDir),
            ["version"] = manifest?.Version ?? "1.0.0",
            ["author"] = manifest?.Author ?? "Unknown",
            ["loadOrder"] = manifest?.LoadOrder ?? 100,
        };

        // Provenance marker: which tool deployed this folder. Lets a deployer clean up
        // its own retired modpacks by scanning Mods/ (stateless) without ever touching
        // mods installed by another tool or by hand.
        if (!string.IsNullOrEmpty(deployedBy))
            runtimeObj["deployedBy"] = deployedBy;

        // Template overrides from stats/*.json → "patches" (+ legacy "templates").
        var patches = new JsonObject();
        var legacyTemplates = new JsonObject();
        var statsDir = Path.Combine(sourceDir, "stats");
        if (Directory.Exists(statsDir))
        {
            foreach (var statsFile in Directory.GetFiles(statsDir, "*.json"))
            {
                var templateType = Path.GetFileNameWithoutExtension(statsFile);
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(statsFile));
                    if (node != null)
                    {
                        patches[templateType] = JsonNode.Parse(node.ToJsonString());
                        legacyTemplates[templateType] = node;
                    }
                }
                catch (Exception ex)
                {
                    // Deploying without the file would silently drop its patches at runtime.
                    progress?.Report($"Warning: stats/{Path.GetFileName(statsFile)} is malformed and was skipped ({ex.Message})");
                }
            }
        }

        // Merge manifest.Patches (only keys not already provided by stats/).
        if (manifest?.Patches != null)
        {
            var patchNode = JsonNode.Parse(JsonSerializer.Serialize(manifest.Patches))?.AsObject();
            if (patchNode != null)
            {
                foreach (var kvp in patchNode)
                {
                    if (!patches.ContainsKey(kvp.Key) && kvp.Value != null)
                        patches[kvp.Key] = JsonNode.Parse(kvp.Value.ToJsonString());
                }
            }
        }

        // Assets: manifest entries, then any unlisted files under assets/.
        var assetsObj = new JsonObject();
        if (manifest?.Assets != null)
        {
            foreach (var kvp in manifest.Assets)
                assetsObj[kvp.Key] = kvp.Value;
        }
        var assetsDir = Path.Combine(sourceDir, "assets");
        if (Directory.Exists(assetsDir))
        {
            foreach (var file in Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories))
            {
                // Use forward slashes so keys match the manifest/loader convention on every OS.
                var relPath = Path.GetRelativePath(assetsDir, file).Replace('\\', '/');
                if (!assetsObj.ContainsKey(relPath))
                    assetsObj[relPath] = "assets/" + relPath;
            }
        }

        // Clones from clones/*.json.
        var clones = new JsonObject();
        var clonesDir = Path.Combine(sourceDir, "clones");
        if (Directory.Exists(clonesDir))
        {
            foreach (var file in Directory.GetFiles(clonesDir, "*.json"))
            {
                var templateType = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(file));
                    if (node != null)
                        clones[templateType] = node;
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: clones/{Path.GetFileName(file)} is malformed and was skipped ({ex.Message})");
                }
            }
        }

        runtimeObj["clones"] = clones;
        runtimeObj["patches"] = patches;
        runtimeObj["templates"] = legacyTemplates; // backward compat for the v1 loader
        runtimeObj["assets"] = assetsObj;

        if (manifest?.Code != null && manifest.Code.HasAnyCode)
        {
            runtimeObj["code"] = new JsonObject
            {
                ["sources"] = new JsonArray(manifest.Code.Sources.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
                ["references"] = new JsonArray(manifest.Code.References.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
                ["prebuiltDlls"] = new JsonArray(manifest.Code.PrebuiltDlls.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
            };
        }

        if (manifest?.Bundles != null && manifest.Bundles.Count > 0)
            runtimeObj["bundles"] = new JsonArray(manifest.Bundles.Select(b => (JsonNode)JsonValue.Create(b)!).ToArray());

        runtimeObj["securityStatus"] = manifest?.SecurityStatus.ToString() ?? "Unreviewed";

        File.WriteAllText(
            Path.Combine(deployDir, "modpack.json"),
            runtimeObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ModpackManifest? LoadManifest(string modpackDir)
    {
        var modpackPath = Path.Combine(modpackDir, "modpack.json");
        var manifestPath = Path.Combine(modpackDir, "manifest.json");
        var infoPath = File.Exists(modpackPath) ? modpackPath : manifestPath;

        var manifest = ModpackManifest.LoadFromFile(infoPath);
        if (manifest == null)
            return null;

        manifest.Path = modpackDir;
        if (string.IsNullOrWhiteSpace(manifest.Name))
            manifest.Name = Path.GetFileName(modpackDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return manifest;
    }
}
