using System.IO;
using System.IO.Compression;
using System.Linq;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>Verifies install (copy folder / dll into Mods/) and uninstall (delete).</summary>
public sealed class ModInstallServiceTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmins-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;
    private readonly TestModkitConfig _config;

    public ModInstallServiceTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        Directory.CreateDirectory(_modsDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Install_ModpackFolder_CopiesIntoMods()
    {
        // A source modpack folder somewhere outside Mods/
        var srcDir = Path.Combine(_gameDir, "src", "CoolPack");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "modpack.json"),
            @"{""manifestVersion"":2,""name"":""Cool Pack"",""version"":""1.0.0""}");

        var installed = new ModInstallService(_config).Install(srcDir);

        Assert.Equal(Path.Combine(_modsDir, "CoolPack"), installed);
        Assert.True(File.Exists(Path.Combine(_modsDir, "CoolPack", "modpack.json")));

        var mod = new ModCatalog(_config).Scan().Single(m => m.Kind == ModKind.Modpack);
        Assert.Equal("Cool Pack", mod.DisplayName);
    }

    [Fact]
    public void Install_ZipWithWrapperFolder_ExtractsIntoMods()
    {
        // Build a zip containing "WOMENACE/jiangyu.json" (the common publish layout).
        var stage = Path.Combine(_gameDir, "stage", "WOMENACE");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "jiangyu.json"),
            @"{""name"":""WOMENACE"",""version"":""1.4.0"",""author"":""pi""}");

        var zipPath = Path.Combine(_gameDir, "WOMENACE-1.4.0.zip");
        ZipFile.CreateFromDirectory(Path.Combine(_gameDir, "stage"), zipPath);

        var installed = new ModInstallService(_config).Install(zipPath);

        Assert.Equal(Path.Combine(_modsDir, "WOMENACE"), installed);
        Assert.True(File.Exists(Path.Combine(_modsDir, "WOMENACE", "jiangyu.json")));

        var mod = new ModCatalog(_config).Scan().Single(m => m.Kind == ModKind.Jiangyu);
        Assert.Equal("WOMENACE", mod.DisplayName);
    }

    [Fact]
    public void Install_AlreadyPresent_ReplacesAsUpdate()
    {
        var srcDir = Path.Combine(_gameDir, "src", "Dup");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "modpack.json"), @"{""manifestVersion"":2,""name"":""Dup""}");

        var svc = new ModInstallService(_config);
        var target = svc.Install(srcDir);

        // Leave a stale file in the installed copy, then reinstall — it should be gone (clean replace).
        File.WriteAllText(Path.Combine(target, "stale.txt"), "old");
        svc.Install(srcDir);

        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(target, "modpack.json")));
    }

    [Fact]
    public void Install_ZipWithSourceTree_StripsSrcBuildJunk()
    {
        // A raw MelonMod zipped with its whole project: "Mod/Mod.dll" + "Mod/src/{bin,obj,cs}".
        var stage = Path.Combine(_gameDir, "stage", "Mount Up");
        Directory.CreateDirectory(Path.Combine(stage, "src", "bin", "Release"));
        Directory.CreateDirectory(Path.Combine(stage, "src", "obj"));
        File.WriteAllText(Path.Combine(stage, "MountUp.dll"), "mod");
        File.WriteAllText(Path.Combine(stage, "src", "MountUpMod.cs"), "// src");
        File.WriteAllText(Path.Combine(stage, "src", "bin", "Release", "MountUpMod.dll"), "dupe");
        File.WriteAllText(Path.Combine(stage, "src", "obj", "MountUpMod.dll"), "dupe");

        var zipPath = Path.Combine(_gameDir, "MountUp.zip");
        ZipFile.CreateFromDirectory(Path.Combine(_gameDir, "stage"), zipPath);

        var installed = new ModInstallService(_config).Install(zipPath);

        Assert.Equal(Path.Combine(_modsDir, "Mount Up"), installed);
        Assert.True(File.Exists(Path.Combine(installed, "MountUp.dll")));
        // The project tree (and its duplicate build-output DLLs) must not be deployed.
        Assert.False(Directory.Exists(Path.Combine(installed, "src")));
    }

    [Fact]
    public void Uninstall_RemovesFromDisk()
    {
        var packDir = Path.Combine(_modsDir, "Gone");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "modpack.json"), @"{""manifestVersion"":2,""name"":""Gone""}");

        var mod = new ModCatalog(_config).Scan().Single();
        new ModInstallService(_config).Uninstall(mod);

        Assert.False(Directory.Exists(packDir));
        Assert.Empty(new ModCatalog(_config).Scan());
    }

    [Fact]
    public void Uninstall_ProtectedMod_Throws()
    {
        FixtureAssembly.Emit(_modsDir, "Menace.ModpackLoader", "namespace X { public class Y { } }");
        var infra = new ModCatalog(_config).Scan().Single(m => m.IsProtected);

        Assert.Throws<InvalidOperationException>(() => new ModInstallService(_config).Uninstall(infra));
    }
}
