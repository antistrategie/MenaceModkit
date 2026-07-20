using System.IO;
using System.Linq;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies enable/disable moves mods between Mods/ and DisabledMods/, that the catalog
/// reflects the location, and that protected mods can't be toggled — all on disk, no ledger.
/// </summary>
public sealed class ModEnableServiceTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmen-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;
    private readonly TestModkitConfig _config;

    public ModEnableServiceTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        Directory.CreateDirectory(_modsDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    [Fact]
    public void Toggle_LeaderPack_PreservesCustomleadersNesting()
    {
        var packDir = Path.Combine(_modsDir, "customleaders", "menace");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "menace_clone.json"), @"{""nickname"":""MENACE""}");

        var svc = new ModEnableService(_config);
        var catalog = new ModCatalog(_config);

        var mod = Assert.Single(catalog.Scan(), m => m.Kind == ModKind.Leader);
        svc.Disable(mod);
        Assert.True(File.Exists(Path.Combine(_gameDir, "DisabledMods", "customleaders", "menace", "menace_clone.json")));
        Assert.False(Directory.Exists(packDir));

        var disabled = Assert.Single(catalog.Scan(), m => m.Kind == ModKind.Leader);
        Assert.False(disabled.IsEnabled);
        svc.Enable(disabled);
        Assert.True(File.Exists(Path.Combine(packDir, "menace_clone.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    private ManagedMod ScanOne(string id) =>
        new ModCatalog(_config).Scan().Single(m => m.Id == id);

    [Fact]
    public void Disable_ThenEnable_MovesModpackFolderBetweenModsAndDisabled()
    {
        var packDir = Path.Combine(_modsDir, "MyPack");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "modpack.json"),
            @"{""manifestVersion"":2,""name"":""My Pack"",""version"":""1.0.0""}");

        var svc = new ModEnableService(_config);

        // Disable → moves to DisabledMods/, catalog shows it disabled
        svc.Disable(ScanOne("My Pack"));
        Assert.False(Directory.Exists(packDir));
        Assert.True(Directory.Exists(Path.Combine(_gameDir, "DisabledMods", "MyPack")));
        Assert.False(ScanOne("My Pack").IsEnabled);

        // Enable → moves back to Mods/
        svc.Enable(ScanOne("My Pack"));
        Assert.True(Directory.Exists(packDir));
        Assert.True(ScanOne("My Pack").IsEnabled);
    }

    [Fact]
    public void Disable_RawMelonDll_MovesFileToDisabled()
    {
        var src = @"
using System;
[assembly: MelonLoader.MelonInfoAttribute(typeof(M.E), ""Raw"", ""1.0"", ""x"")]
namespace MelonLoader {
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
}
namespace M { public class E { } }";
        FixtureAssembly.Emit(_modsDir, "RawMod", src);

        new ModEnableService(_config).Disable(ScanOne("RawMod.dll"));

        Assert.False(File.Exists(Path.Combine(_modsDir, "RawMod.dll")));
        Assert.True(File.Exists(Path.Combine(_gameDir, "DisabledMods", "RawMod.dll")));
        Assert.False(ScanOne("RawMod.dll").IsEnabled);
    }

    [Fact]
    public void Enable_SuffixedDllParkedInDisabledMods_StripsSuffix()
    {
        // A ".dll.disabled" file that ended up in DisabledMods/ must come back as a
        // plain ".dll" — moving it with the suffix intact would leave it in Mods/
        // still disabled after the user clicked Enable.
        var disabledDir = Path.Combine(_gameDir, "DisabledMods");
        Directory.CreateDirectory(disabledDir);
        var parked = Path.Combine(disabledDir, "Weird.dll.disabled");
        File.WriteAllText(parked, "x");

        var mod = new ManagedMod
        {
            Kind = ModKind.MelonMod,
            Id = "Weird.dll.disabled",
            DisplayName = "Weird",
            IsEnabled = false,
            Location = parked,
        };

        new ModEnableService(_config).Enable(mod);

        Assert.True(File.Exists(Path.Combine(_modsDir, "Weird.dll")));
        Assert.False(File.Exists(parked));
    }

    [Fact]
    public void SetEnabled_ProtectedMod_Throws()
    {
        FixtureAssembly.Emit(_modsDir, "Menace.ModpackLoader", "namespace X { public class Y { } }");
        var infra = ScanOne("Menace.ModpackLoader.dll");

        Assert.Throws<InvalidOperationException>(() => new ModEnableService(_config).Disable(infra));
    }
}
