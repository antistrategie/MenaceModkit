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

    [Fact]
    public void InstallFrom_BareMelonFolder_HoistsDllToModsRoot()
    {
        // Raw MelonMods only load as top-level Mods/*.dll — a bare folder (no
        // modpack.json/jiangyu.json) must not be installed as a folder.
        var src = Path.Combine(_gameDir, "incoming", "Mount Up");
        Directory.CreateDirectory(Path.Combine(src, "src", "bin"));
        File.WriteAllText(Path.Combine(src, "MountUp.dll"), "melon");
        File.WriteAllText(Path.Combine(src, "src", "bin", "MountUp.dll"), "stale");
        File.WriteAllText(Path.Combine(src, "README.txt"), "docs");

        new ModInstallService(_config).InstallFrom(src, "Mount Up");

        Assert.True(File.Exists(Path.Combine(_modsDir, "MountUp.dll")));
        Assert.False(Directory.Exists(Path.Combine(_modsDir, "Mount Up")));
    }

    [Fact]
    public void InstallFrom_ModWithDependencyDll_SendsDependencyToUserLibs()
    {
        // A raw MelonMod shipped with a plain library beside it: the melon hoists to
        // Mods/ root, the dependency goes to UserLibs/ (MelonLoader's resolve dir) —
        // hoisting it too would list the dep as a separate mod in the catalog.
        var src = Path.Combine(_gameDir, "incoming", "NetMod");
        Directory.CreateDirectory(src);
        FixtureAssembly.Emit(src, "NetMod", @"
using System;
[assembly: MelonLoader.MelonInfoAttribute(typeof(M.E), ""NetMod"", ""1.0"", ""x"")]
namespace MelonLoader {
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
}
namespace M { public class E { } }");
        FixtureAssembly.Emit(src, "LiteNetLib", "namespace L { public class N { } }");

        new ModInstallService(_config).InstallFrom(src, "NetMod");

        Assert.True(File.Exists(Path.Combine(_modsDir, "NetMod.dll")));
        Assert.False(File.Exists(Path.Combine(_modsDir, "LiteNetLib.dll")));
        Assert.True(File.Exists(Path.Combine(_gameDir, "UserLibs", "LiteNetLib.dll")));
    }

    [Fact]
    public void InstallFrom_MelonFolderWithAssets_HoistsDllAndKeepsAssetFolder()
    {
        var src = Path.Combine(_gameDir, "incoming", "AssetMod");
        Directory.CreateDirectory(Path.Combine(src, "data"));
        File.WriteAllText(Path.Combine(src, "AssetMod.dll"), "melon");
        File.WriteAllText(Path.Combine(src, "data", "table.json"), "{}");

        new ModInstallService(_config).InstallFrom(src, "AssetMod");

        Assert.True(File.Exists(Path.Combine(_modsDir, "AssetMod.dll")));
        Assert.True(File.Exists(Path.Combine(_modsDir, "AssetMod", "data", "table.json")));
        Assert.False(File.Exists(Path.Combine(_modsDir, "AssetMod", "AssetMod.dll"))); // not duplicated
    }

    [Fact]
    public void InstallFrom_ManifestFolder_StaysAFolder()
    {
        var src = Path.Combine(_gameDir, "incoming", "Pack");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "modpack.json"), @"{""name"":""Pack""}");
        File.WriteAllText(Path.Combine(src, "Pack.dll"), "code");

        new ModInstallService(_config).InstallFrom(src, "Pack");

        Assert.True(File.Exists(Path.Combine(_modsDir, "Pack", "modpack.json")));
        Assert.False(File.Exists(Path.Combine(_modsDir, "Pack.dll")));
    }

    [Fact]
    public void InstallFrom_CustomLeaderFrameworkBundle_HoistsDllAndSkipsExamplePacks()
    {
        // The CustomLeader zip shape: framework DLL + customleaders/<pack>/ + tools/ + README.
        var src = Path.Combine(_gameDir, "incoming", "CustomLeader");
        Directory.CreateDirectory(Path.Combine(src, "customleaders", "menace"));
        Directory.CreateDirectory(Path.Combine(src, "tools"));
        File.WriteAllText(Path.Combine(src, "MenaceCustomLeader.dll"), "melon");
        File.WriteAllText(Path.Combine(src, "README.txt"), "docs");
        File.WriteAllText(Path.Combine(src, "customleaders", "menace", "menace_clone.json"), @"{""nickname"":""MENACE""}");
        File.WriteAllText(Path.Combine(src, "tools", "template.png"), "img");

        new ModInstallService(_config).InstallFrom(src, "CustomLeader");

        Assert.True(File.Exists(Path.Combine(_modsDir, "MenaceCustomLeader.dll")));
        // Example content bundled with a framework DLL is NOT installed — users add
        // the leader packs they actually want separately.
        Assert.False(Directory.Exists(Path.Combine(_modsDir, "customleaders", "menace")));
        Assert.False(Directory.Exists(Path.Combine(_modsDir, "CustomLeader"))); // no leftover folder
    }

    [Fact]
    public void InstallFrom_BareLeaderPack_InstallsUnderCustomleaders()
    {
        var src = Path.Combine(_gameDir, "incoming", "jane");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "jane_replace.json"), @"{""nickname"":""Jane""}");
        File.WriteAllText(Path.Combine(src, "jane_162x162.png"), "img");

        new ModInstallService(_config).InstallFrom(src, "jane");

        Assert.True(File.Exists(Path.Combine(_modsDir, "customleaders", "jane", "jane_replace.json")));
        Assert.False(Directory.Exists(Path.Combine(_modsDir, "jane")));
    }

    [Fact]
    public void InstallFrom_CustomleadersRootArchive_MergesWithoutWipingExisting()
    {
        // The common multi-leader distribution: the archive root IS customleaders/.
        var existing = Path.Combine(_modsDir, "customleaders", "menace");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "menace_clone.json"), "{}");

        var src = Path.Combine(_gameDir, "incoming", "customleaders");
        Directory.CreateDirectory(Path.Combine(src, "anis"));
        Directory.CreateDirectory(Path.Combine(src, "clay"));
        File.WriteAllText(Path.Combine(src, "anis", "anis_clone.json"), "{}");
        File.WriteAllText(Path.Combine(src, "clay", "clay_clone.json"), "{}");

        new ModInstallService(_config).InstallFrom(src, "customleaders");

        Assert.True(File.Exists(Path.Combine(_modsDir, "customleaders", "anis", "anis_clone.json")));
        Assert.True(File.Exists(Path.Combine(_modsDir, "customleaders", "clay", "clay_clone.json")));
        Assert.True(File.Exists(Path.Combine(existing, "menace_clone.json"))); // untouched
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

        // Archives route through InstallFrom, so the DLL is hoisted to the Mods/ root
        // (MelonLoader only loads top-level Mods/*.dll — a "Mount Up/" folder would
        // never be read) and the project tree is not deployed at all.
        Assert.Equal(Path.Combine(_modsDir, "MountUp.dll"), installed);
        Assert.True(File.Exists(installed));
        Assert.False(Directory.Exists(Path.Combine(_modsDir, "Mount Up")));
    }

    [Fact]
    public void Install_ZipWithCustomleadersRoot_MergesInsteadOfWiping()
    {
        // Pre-existing leader pack installed by the user.
        var existing = Path.Combine(_modsDir, "customleaders", "Darby");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "darby_clone.json"), "{}");

        // A zip whose root is a customleaders/ folder (the common multi-leader shape).
        var stage = Path.Combine(_gameDir, "stage-leaders", "customleaders", "Nikke");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "nikke_clone.json"), "{}");

        var zipPath = Path.Combine(_gameDir, "Leaders.zip");
        ZipFile.CreateFromDirectory(Path.Combine(_gameDir, "stage-leaders"), zipPath);

        new ModInstallService(_config).Install(zipPath);

        // The new pack merged in — and the pre-existing pack survived. (Routing this
        // shape through PlaceNamed would have replaced the whole customleaders/ root.)
        Assert.True(File.Exists(Path.Combine(_modsDir, "customleaders", "Nikke", "nikke_clone.json")));
        Assert.True(File.Exists(Path.Combine(existing, "darby_clone.json")));
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
