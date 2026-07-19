using System.IO;
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
    public void Install_AlreadyPresent_Throws()
    {
        var srcDir = Path.Combine(_gameDir, "src", "Dup");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "modpack.json"), @"{""manifestVersion"":2,""name"":""Dup""}");

        var svc = new ModInstallService(_config);
        svc.Install(srcDir);

        Assert.Throws<IOException>(() => svc.Install(srcDir));
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
