using System;
using System.IO;
using System.Linq;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies the scan spots two Jiangyu folders declaring one manifest name — the state a
/// load-order rename plus an author redeploy leaves behind, which the loader refuses to
/// resolve on its own.
/// </summary>
public sealed class DuplicateModDetectorTests : IDisposable
{
    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmdup-" + Guid.NewGuid().ToString("N"));
    private readonly string _modsDir;
    private readonly TestModkitConfig _config;

    public DuplicateModDetectorTests()
    {
        _modsDir = Path.Combine(_gameDir, "Mods");
        Directory.CreateDirectory(_modsDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    private void MakeJiangyuMod(string folder, string name, DateTime writtenUtc)
    {
        var dir = Path.Combine(_modsDir, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "jiangyu.json"),
            $@"{{""name"":""{name}"",""version"":""1.0.0""}}");
        Directory.SetLastWriteTimeUtc(dir, writtenUtc);
    }

    [Fact]
    public void Find_PairsFoldersSharingAManifestName_AndKeepsTheNewest()
    {
        MakeJiangyuMod("010-WOMENACE", "WOMENACE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        MakeJiangyuMod("WOMENACE", "WOMENACE", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var group = Assert.Single(DuplicateModDetector.Find(new ModCatalog(_config).Scan()));

        Assert.Equal("WOMENACE", group.Name);
        Assert.Equal("WOMENACE", new DirectoryInfo(group.Keep.Location).Name);
        Assert.Equal("010-WOMENACE", new DirectoryInfo(Assert.Single(group.Stale).Location).Name);
    }

    [Fact]
    public void Find_IgnoresDistinctNames()
    {
        MakeJiangyuMod("010-alpha", "Alpha", DateTime.UtcNow);
        MakeJiangyuMod("020-beta", "Beta", DateTime.UtcNow);

        Assert.Empty(DuplicateModDetector.Find(new ModCatalog(_config).Scan()));
    }

    [Fact]
    public void Find_IgnoresDisabledCopies()
    {
        MakeJiangyuMod("WOMENACE", "WOMENACE", DateTime.UtcNow);

        var disabled = Path.Combine(_gameDir, "DisabledMods", "010-WOMENACE");
        Directory.CreateDirectory(disabled);
        File.WriteAllText(Path.Combine(disabled, "jiangyu.json"),
            @"{""name"":""WOMENACE"",""version"":""1.0.0""}");

        // A disabled copy sits outside Mods/, so the loader never sees it and it cannot collide.
        Assert.Empty(DuplicateModDetector.Find(new ModCatalog(_config).Scan()));
    }

    [Fact]
    public void Describe_NamesBothFoldersAndTheConsequence()
    {
        MakeJiangyuMod("010-WOMENACE", "WOMENACE", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        MakeJiangyuMod("WOMENACE", "WOMENACE", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var description = DuplicateModDetector.Find(new ModCatalog(_config).Scan()).Single().Describe();

        Assert.Contains("010-WOMENACE", description);
        Assert.Contains("none of them load", description);
    }
}
