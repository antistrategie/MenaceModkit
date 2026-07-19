using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>Verifies MelonLoader version listing + install against stubbed GitHub responses (offline).</summary>
public sealed class MelonLoaderInstallerTests : IDisposable
{
    private const string ReleasesApi = "https://api.github.com/repos/LavaGang/MelonLoader/releases";

    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmml-" + Guid.NewGuid().ToString("N"));
    private readonly TestModkitConfig _config;

    public MelonLoaderInstallerTests()
    {
        Directory.CreateDirectory(_gameDir);
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    // v0.9.1 is unsupported; v0.7.3 latest.
    private const string ReleasesJson = @"[
      { ""tag_name"": ""v0.7.3"", ""draft"": false, ""prerelease"": false,
        ""assets"": [
          { ""name"": ""MelonLoader.x64.zip"", ""browser_download_url"": ""https://x/0.7.3/MelonLoader.x64.zip"" },
          { ""name"": ""MelonLoader.Linux.x64.zip"", ""browser_download_url"": ""https://x/0.7.3/linux.zip"" }
        ] },
      { ""tag_name"": ""v0.9.1"", ""draft"": false, ""prerelease"": false,
        ""assets"": [ { ""name"": ""MelonLoader.x64.zip"", ""browser_download_url"": ""https://x/0.9.1/MelonLoader.x64.zip"" } ] },
      { ""tag_name"": ""v0.7.2"", ""draft"": false, ""prerelease"": false,
        ""assets"": [ { ""name"": ""MelonLoader.x64.zip"", ""browser_download_url"": ""https://x/0.7.2/MelonLoader.x64.zip"" } ] }
    ]";

    /// <summary>Build a MelonLoader-shaped zip (version.dll + MelonLoader/ tree) and return its bytes.</summary>
    private byte[] BuildMelonLoaderZip()
    {
        var stage = Path.Combine(_gameDir, "stage");
        Directory.CreateDirectory(Path.Combine(stage, "MelonLoader", "net6"));
        File.WriteAllText(Path.Combine(stage, "version.dll"), "proxy");
        File.WriteAllText(Path.Combine(stage, "MelonLoader", "net6", "MelonLoader.dll"), "core");

        var zipPath = Path.Combine(_gameDir, "ml.zip");
        ZipFile.CreateFromDirectory(stage, zipPath);
        var bytes = File.ReadAllBytes(zipPath);

        Directory.Delete(stage, recursive: true);
        File.Delete(zipPath);
        return bytes;
    }

    [Fact]
    public async Task ListVersions_ExcludesUnsupported()
    {
        var installer = new MelonLoaderInstaller(_config, new StubHttpHandler().Json(ReleasesApi, ReleasesJson));

        var versions = await installer.ListVersionsAsync();

        Assert.Equal(new[] { "v0.7.3", "v0.7.2" }, versions);
        Assert.DoesNotContain("v0.9.1", versions);
    }

    [Fact]
    public async Task InstallLatest_ExtractsWindowsPackageIntoGameDir()
    {
        var handler = new StubHttpHandler()
            .Json(ReleasesApi, ReleasesJson)
            .Bytes("https://x/0.7.3/MelonLoader.x64.zip", BuildMelonLoaderZip());
        var installer = new MelonLoaderInstaller(_config, handler);

        await installer.InstallAsync(null);

        Assert.True(File.Exists(Path.Combine(_gameDir, "version.dll")));
        Assert.True(File.Exists(Path.Combine(_gameDir, "MelonLoader", "net6", "MelonLoader.dll")));
    }

    [Fact]
    public async Task Install_UnsupportedVersion_Throws()
    {
        var installer = new MelonLoaderInstaller(_config, new StubHttpHandler().Json(ReleasesApi, ReleasesJson));

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("v0.9.1"));
    }
}
