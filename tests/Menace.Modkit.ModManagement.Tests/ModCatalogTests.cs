using System.IO;
using System.Linq;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies the live Mods/ scan: modpacks, raw MelonMods, Jiangyu mods, disabled mods,
/// infrastructure, and skipped system DLLs — all derived from the filesystem, no ledger.
/// </summary>
public sealed class ModCatalogTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmcat-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;

    public ModCatalogTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        Directory.CreateDirectory(_modsDir);
    }

    [Fact]
    public void Scan_ListsLeaderPacks_WithNicknameAndDisabledMirror()
    {
        Directory.CreateDirectory(Path.Combine(_modsDir, "customleaders", "menace"));
        File.WriteAllText(Path.Combine(_modsDir, "customleaders", "menace", "menace_clone.json"),
            @"{""nickname"":""MENACE"",""clone_from"":""Darby""}");
        var disabledDir = Path.Combine(_gameDir, "DisabledMods", "customleaders", "jane");
        Directory.CreateDirectory(disabledDir);
        File.WriteAllText(Path.Combine(disabledDir, "jane_replace.json"), @"{""nickname"":""Jane""}");

        var mods = new ModCatalog(new TestModkitConfig { GameInstallPath = _gameDir }).Scan();

        var menace = Assert.Single(mods, m => m.Kind == ModKind.Leader && m.IsEnabled);
        Assert.Equal("MENACE", menace.DisplayName);
        Assert.Equal("clones Darby", menace.Author);
        var jane = Assert.Single(mods, m => m.Kind == ModKind.Leader && !m.IsEnabled);
        Assert.Equal("Jane", jane.DisplayName);
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    private ModCatalog NewCatalog() =>
        new(new TestModkitConfig { GameInstallPath = _gameDir });

    private const string MelonModSource = @"
using System;
[assembly: MelonLoader.MelonInfoAttribute(typeof(M.E), ""{NAME}"", ""{VER}"", ""{AUTH}"")]
namespace MelonLoader {
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
}
namespace M { public class E { } }";

    private void EmitMelonMod(string assemblyName, string name, string version, string author)
    {
        var src = MelonModSource
            .Replace("{NAME}", name).Replace("{VER}", version).Replace("{AUTH}", author);
        FixtureAssembly.Emit(_modsDir, assemblyName, src);
    }

    [Fact]
    public void Scan_MixedModsDirectory_ClassifiesEverything()
    {
        // Modpack folder
        var packDir = Path.Combine(_modsDir, "MyPack");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "modpack.json"),
            @"{""manifestVersion"":2,""name"":""My Pack"",""version"":""1.2.0"",""author"":""Me""}");

        // Enabled raw MelonMod
        EmitMelonMod("CoolMod", "Cool Mod", "3.0.0", "Jane");

        // Disabled MelonMod (emit then rename to .dll.disabled)
        EmitMelonMod("OldMod", "Old Mod", "0.9.0", "Bob");
        File.Move(Path.Combine(_modsDir, "OldMod.dll"), Path.Combine(_modsDir, "OldMod.dll.disabled"));

        // Infrastructure DLL (name-based classification; no MelonInfo needed)
        FixtureAssembly.Emit(_modsDir, "Menace.ModpackLoader", "namespace X { public class Y { } }");

        // System DLL — should be skipped (not even a valid PE)
        File.WriteAllText(Path.Combine(_modsDir, "System.Fake.dll"), "not a real assembly");

        var mods = NewCatalog().Scan();

        // System.Fake skipped → 4 mods
        Assert.Equal(4, mods.Count);

        var pack = mods.Single(m => m.Kind == ModKind.Modpack);
        Assert.Equal("My Pack", pack.DisplayName);
        Assert.Equal("1.2.0", pack.Version);
        Assert.True(pack.IsEnabled);

        var cool = mods.Single(m => m.Id == "CoolMod.dll");
        Assert.Equal(ModKind.MelonMod, cool.Kind);
        Assert.Equal("Cool Mod", cool.DisplayName);
        Assert.Equal("3.0.0", cool.Version);
        Assert.True(cool.IsEnabled);

        var old = mods.Single(m => m.Id == "OldMod.dll");
        Assert.False(old.IsEnabled);
        Assert.Equal("Old Mod", old.DisplayName);

        var infra = mods.Single(m => m.Kind == ModKind.Infrastructure);
        Assert.Equal("Menace.ModpackLoader.dll", infra.Id);
        Assert.True(infra.IsProtected);
    }

    [Fact]
    public void Scan_JiangyuModFolder_ClassifiedAsJiangyu()
    {
        // A Jiangyu mod is a folder with jiangyu.json (code/, bundles/, locales/) — not modpack.json.
        var modDir = Path.Combine(_modsDir, "WOMENACE");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "jiangyu.json"),
            @"{""name"":""WOMENACE"",""version"":""1.4.0"",""author"":""pi"",""compiledForJiangyu"":""1.2.0""}");

        var jiangyu = NewCatalog().Scan().Single(m => m.Kind == ModKind.Jiangyu);

        Assert.Equal("WOMENACE", jiangyu.DisplayName);
        Assert.Equal("1.4.0", jiangyu.Version);
        Assert.Equal("pi", jiangyu.Author);
        Assert.Equal("1.2.0", jiangyu.CompiledForJiangyu);
        Assert.Contains("JY 1.2.0", jiangyu.VersionDisplay);
        Assert.True(jiangyu.IsEnabled);
    }

    [Fact]
    public void Scan_RawMelonModFolder_WithAssets_IsDetected()
    {
        // A raw MelonMod shipped as a folder: MyMod.dll + an assets/ subfolder, no manifest.
        var modDir = Path.Combine(_modsDir, "FolderMod");
        Directory.CreateDirectory(Path.Combine(modDir, "assets"));
        File.WriteAllText(Path.Combine(modDir, "assets", "data.txt"), "x");
        EmitMelonMod("FolderMod", "Folder Mod", "2.0.0", "dev"); // lands in Mods/ root...
        File.Move(Path.Combine(_modsDir, "FolderMod.dll"), Path.Combine(modDir, "FolderMod.dll"));

        var mod = NewCatalog().Scan().Single(m => m.Id == "FolderMod");

        Assert.Equal(ModKind.MelonMod, mod.Kind);
        Assert.Equal("Folder Mod", mod.DisplayName);
        Assert.Equal("2.0.0", mod.Version);
        Assert.Equal(modDir, mod.Location);
    }

    [Fact]
    public void Scan_JiangyuLoaderDll_IsInfrastructure()
    {
        // The Jiangyu *loader* (a loose Jiangyu.Loader.dll) is infrastructure, not a manageable mod.
        FixtureAssembly.Emit(_modsDir, "Jiangyu.Loader", "namespace J { public class L { } }");

        var loader = NewCatalog().Scan().Single(m => m.Id == "Jiangyu.Loader.dll");

        Assert.Equal(ModKind.Infrastructure, loader.Kind);
        Assert.True(loader.IsProtected);
    }

    [Fact]
    public void Scan_MalformedModpackJson_DoesNotThrowAndStillLists()
    {
        // A bad modpack.json must not crash the scan (which would fail app launch).
        var packDir = Path.Combine(_modsDir, "BadPack");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "modpack.json"), "{ this is not valid json ");

        var mods = NewCatalog().Scan(); // must not throw

        Assert.Contains(mods, m => m.Kind == ModKind.Modpack && m.Id == "BadPack");
    }

    [Fact]
    public void Scan_MelonModDllInSubfolder_IsDetected()
    {
        // Raw MelonMod shipped as MyMod/bin/mod.dll (no manifest, DLL not at the folder root).
        var modDir = Path.Combine(_modsDir, "SubMod");
        Directory.CreateDirectory(Path.Combine(modDir, "bin"));
        EmitMelonMod("SubMod", "Sub Mod", "1.0.0", "dev"); // lands at Mods/SubMod.dll…
        File.Move(Path.Combine(_modsDir, "SubMod.dll"), Path.Combine(modDir, "bin", "SubMod.dll"));

        var mod = NewCatalog().Scan().Single(m => m.Id == "SubMod");

        Assert.Equal(ModKind.MelonMod, mod.Kind);
        Assert.Equal("Sub Mod", mod.DisplayName);
    }

    [Fact]
    public void Scan_NoGameLocated_ReturnsEmpty()
    {
        var catalog = new ModCatalog(new TestModkitConfig { GameInstallPath = null });
        Assert.Empty(catalog.Scan());
    }

    [Fact]
    public void Scan_EmptyModsDirectory_ReturnsEmpty()
    {
        Assert.Empty(NewCatalog().Scan());
    }
}
