using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies the loose, stateless deploy: copy the mod in (minus source folders), merge
/// stats/clones into the runtime modpack.json. (The compile path needs the game's reference
/// assemblies, so these cover data/asset modpacks with no C# sources.)
/// </summary>
public sealed class ModDeployServiceTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmdep-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;
    private readonly string _sourceDir;
    private readonly TestModkitConfig _config;

    public ModDeployServiceTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        _sourceDir = Path.Combine(_gameDir, "src", "MyPack");
        Directory.CreateDirectory(_modsDir);
        Directory.CreateDirectory(_sourceDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteManifest(string json) =>
        File.WriteAllText(Path.Combine(_sourceDir, "modpack.json"), json);

    [Fact]
    public async Task Deploy_MergesStatsIntoRuntimeManifestPatches()
    {
        WriteManifest(@"{""manifestVersion"":2,""name"":""My Pack"",""version"":""1.0.0""}");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "stats"));
        File.WriteAllText(Path.Combine(_sourceDir, "stats", "WeaponTemplate.json"),
            @"{""weapon.smg"":{""Damage"":42}}");

        var target = await new ModDeployService(_config).DeployAsync(_sourceDir);

        Assert.Equal(Path.Combine(_modsDir, "My Pack"), target);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "modpack.json")));
        var patches = doc.RootElement.GetProperty("patches");
        Assert.True(patches.TryGetProperty("WeaponTemplate", out var wt));
        Assert.Equal(42, wt.GetProperty("weapon.smg").GetProperty("Damage").GetInt32());

        // The catalog now sees it as a deployed modpack.
        Assert.Contains(new ModCatalog(_config).Scan(), m => m.Kind == ModKind.Modpack && m.DisplayName == "My Pack");
    }

    [Fact]
    public async Task Deploy_ExcludesSourceAndBuildFolders()
    {
        // No Code.Sources → no compile; src/ and build/ must not be copied to Mods/.
        WriteManifest(@"{""manifestVersion"":2,""name"":""Clean"",""version"":""1.0.0""}");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        File.WriteAllText(Path.Combine(_sourceDir, "src", "Plugin.cs"), "// source");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "build"));
        File.WriteAllText(Path.Combine(_sourceDir, "build", "stale.dll"), "x");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "assets"));
        File.WriteAllText(Path.Combine(_sourceDir, "assets", "icon.png"), "img");

        var target = await new ModDeployService(_config).DeployAsync(_sourceDir);

        Assert.False(Directory.Exists(Path.Combine(target, "src")));
        Assert.False(Directory.Exists(Path.Combine(target, "build")));
        Assert.True(File.Exists(Path.Combine(target, "assets", "icon.png")));
    }

    [Fact]
    public async Task Deploy_SanitisesTraversalName()
    {
        WriteManifest(@"{""manifestVersion"":2,""name"":""../../evil""}");

        var target = await new ModDeployService(_config).DeployAsync(_sourceDir);

        // Name reduced to a plain folder under Mods/ — never escapes it.
        Assert.Equal(_modsDir, Path.GetDirectoryName(target));
        Assert.False(Directory.Exists(Path.Combine(_gameDir, "evil")));
    }

    [Fact]
    public async Task Deploy_SourceInsideMods_ThrowsAndPreservesSource()
    {
        var src = Path.Combine(_modsDir, "InPlace");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "modpack.json"), @"{""manifestVersion"":2,""name"":""InPlace""}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ModDeployService(_config).DeployAsync(src));

        Assert.True(File.Exists(Path.Combine(src, "modpack.json"))); // not deleted
    }

    [Fact]
    public async Task Deploy_KeepsNestedFolderNamedLikeAnArtefact()
    {
        WriteManifest(@"{""manifestVersion"":2,""name"":""Nested""}");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "assets", "obj")); // 'obj' only excluded at root
        File.WriteAllText(Path.Combine(_sourceDir, "assets", "obj", "model.obj"), "v");

        var target = await new ModDeployService(_config).DeployAsync(_sourceDir);

        Assert.True(File.Exists(Path.Combine(target, "assets", "obj", "model.obj")));
    }

    [Fact]
    public async Task Deploy_ReplacesExistingInstall()
    {
        WriteManifest(@"{""manifestVersion"":2,""name"":""Dup"",""version"":""1.0.0""}");

        // Pre-existing stale install with a file that should be gone after redeploy.
        var target = Path.Combine(_modsDir, "Dup");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "stale.txt"), "old");

        await new ModDeployService(_config).DeployAsync(_sourceDir);

        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(target, "modpack.json")));
    }
}
