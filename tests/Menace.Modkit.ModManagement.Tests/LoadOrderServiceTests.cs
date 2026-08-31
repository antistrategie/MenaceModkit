using System.IO;
using System.Linq;
using System.Text.Json;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies load-order edits land in the deployed modpack.json without disturbing the rest
/// of it, and that the catalog reads the new order back — no ledger, disk is the truth.
/// </summary>
public sealed class LoadOrderServiceTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmlo-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;
    private readonly TestModkitConfig _config;

    public LoadOrderServiceTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        Directory.CreateDirectory(_modsDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    private string MakePack(string folder, string name, string extraJson = "")
    {
        var dir = Path.Combine(_modsDir, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "modpack.json"),
            $@"{{""manifestVersion"":2,""name"":""{name}"",""version"":""1.0.0""{extraJson}}}");
        return dir;
    }

    private ManagedMod Scan(string id) => new ModCatalog(_config).Scan().Single(m => m.Id == id);

    private JsonDocument ReadManifest(string dir) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "modpack.json")));

    [Fact]
    public void SetLoadOrder_WritesTheValue_AndCatalogReadsItBack()
    {
        MakePack("A", "Pack A");

        Assert.True(new LoadOrderService(_config).SetLoadOrder(Scan("Pack A"), 250));

        Assert.Equal(250, Scan("Pack A").LoadOrder);
    }

    [Fact]
    public void SetLoadOrder_IsANoOp_WhenTheValueAlreadyMatches()
    {
        MakePack("A", "Pack A", @",""loadOrder"":40");
        var path = Path.Combine(_modsDir, "A", "modpack.json");
        var before = File.ReadAllText(path);

        Assert.False(new LoadOrderService(_config).SetLoadOrder(Scan("Pack A"), 40));

        Assert.Equal(before, File.ReadAllText(path));
    }

    /// <summary>
    /// The deployed manifest carries keys ModpackManifest has no property for. A round-trip
    /// through the typed model would drop them, which is why the edit is a surgical one.
    /// </summary>
    [Fact]
    public void SetLoadOrder_PreservesKeysTheTypedModelDoesNotKnow()
    {
        var dir = MakePack("A", "Pack A",
            @",""loadOrder"":100,
              ""deployedBy"":""standalone"",
              ""clones"":{""weapon"":{""my_gun"":""base_gun""}},
              ""templates"":{""weapon"":{""base_gun"":{""damage"":5}}},
              ""futureKeyTheLoaderAdds"":[1,2,3]");

        new LoadOrderService(_config).SetLoadOrder(Scan("Pack A"), 30);

        using var doc = ReadManifest(dir);
        var root = doc.RootElement;
        Assert.Equal(30, root.GetProperty("loadOrder").GetInt32());
        Assert.Equal("standalone", root.GetProperty("deployedBy").GetString());
        Assert.Equal("base_gun", root.GetProperty("clones").GetProperty("weapon").GetProperty("my_gun").GetString());
        Assert.Equal(5, root.GetProperty("templates").GetProperty("weapon").GetProperty("base_gun").GetProperty("damage").GetInt32());
        Assert.Equal(3, root.GetProperty("futureKeyTheLoaderAdds").GetArrayLength());
    }

    /// <summary>A hand-written manifest may use "LoadOrder"; the edit must replace it, not duplicate it.</summary>
    [Fact]
    public void SetLoadOrder_ReplacesADifferentlyCasedKey()
    {
        var dir = MakePack("A", "Pack A", @",""LoadOrder"":5");

        new LoadOrderService(_config).SetLoadOrder(Scan("Pack A"), 70);

        using var doc = ReadManifest(dir);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Single(keys, k => string.Equals(k, "loadOrder", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(70, doc.RootElement.GetProperty("loadOrder").GetInt32());
    }

    [Fact]
    public void ApplyOrdering_RenumbersInSteps()
    {
        MakePack("A", "Pack A");
        MakePack("B", "Pack B");
        MakePack("C", "Pack C");

        var catalog = new ModCatalog(_config).Scan().Where(m => m.SupportsLoadOrder).ToList();
        var ordered = new[] { "Pack C", "Pack A", "Pack B" }
            .Select(n => catalog.Single(m => m.Id == n)).ToList();

        var written = new LoadOrderService(_config).ApplyOrdering(ordered);

        Assert.Equal(3, written.Count);
        Assert.Equal(10, Scan("Pack C").LoadOrder);
        Assert.Equal(20, Scan("Pack A").LoadOrder);
        Assert.Equal(30, Scan("Pack B").LoadOrder);
    }

    [Fact]
    public void ApplyOrdering_OnlyReportsManifestsItActuallyChanged()
    {
        MakePack("A", "Pack A", @",""loadOrder"":10");
        MakePack("B", "Pack B", @",""loadOrder"":999");

        var catalog = new ModCatalog(_config).Scan().Where(m => m.SupportsLoadOrder).ToList();
        var ordered = new[] { "Pack A", "Pack B" }.Select(n => catalog.Single(m => m.Id == n)).ToList();

        // Pack A is already at 10, so only Pack B (999 → 20) is rewritten.
        Assert.Equal(new[] { "Pack B" }, new LoadOrderService(_config).ApplyOrdering(ordered));
    }

    /// <summary>A malformed manifest must not abandon the rest of the batch half-renumbered.</summary>
    [Fact]
    public void ApplyOrdering_AppliesTheRest_WhenOneManifestIsMalformed()
    {
        MakePack("A", "Pack A");
        var badDir = Path.Combine(_modsDir, "Bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "modpack.json"), "{ not json");
        MakePack("C", "Pack C");

        var ordered = new ModCatalog(_config).Scan().Where(m => m.SupportsLoadOrder)
            .OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(3, ordered.Count); // the malformed pack still lists, under its folder name

        var ex = Assert.Throws<AggregateLoadOrderException>(
            () => new LoadOrderService(_config).ApplyOrdering(ordered));

        Assert.Single(ex.Failures);
        Assert.Equal(2, ex.Written.Count);
        // Ordinal by id puts the malformed "Bad" folder first, so its slot (10) is simply
        // skipped — the packs that could be written keep the number their position implies.
        Assert.Equal(20, Scan("Pack A").LoadOrder);
        Assert.Equal(30, Scan("Pack C").LoadOrder);
    }

    [Fact]
    public void SetLoadOrder_RefusesNonModpacks()
    {
        // A bare DLL is a MelonMod: ordered by its compiled [MelonPriority], not by us.
        File.WriteAllBytes(Path.Combine(_modsDir, "Some.Mod.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

        var mod = Scan("Some.Mod.dll");
        Assert.False(mod.SupportsLoadOrder);
        Assert.Throws<InvalidOperationException>(() => new LoadOrderService(_config).SetLoadOrder(mod, 10));
    }

    [Fact]
    public void SetLoadOrder_WorksOnADisabledModpack()
    {
        MakePack("A", "Pack A");
        var svc = new LoadOrderService(_config);
        new ModEnableService(_config).Disable(Scan("Pack A"));

        svc.SetLoadOrder(Scan("Pack A"), 60);

        var disabled = Scan("Pack A");
        Assert.False(disabled.IsEnabled);
        Assert.Equal(60, disabled.LoadOrder);
    }

    [Fact]
    public void SetLoadOrder_RefusesToWriteOutsideTheGameModFolders()
    {
        var outside = Path.Combine(_gameDir, "elsewhere", "Pack");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "modpack.json"), @"{""manifestVersion"":2,""name"":""X""}");

        var mod = new ManagedMod
        {
            Kind = ModKind.Modpack,
            Id = "X",
            DisplayName = "X",
            IsEnabled = true,
            Location = outside,
        };

        Assert.Throws<InvalidOperationException>(() => new LoadOrderService(_config).SetLoadOrder(mod, 10));
    }
}
