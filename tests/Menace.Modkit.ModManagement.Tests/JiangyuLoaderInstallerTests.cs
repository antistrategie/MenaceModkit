using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>Verifies Jiangyu loader listing + install against stubbed GitHub responses (offline).</summary>
public sealed class JiangyuLoaderInstallerTests : IDisposable
{
    private const string ReleasesApi = "https://api.github.com/repos/antistrategie/jiangyu/releases";

    private readonly string _gameDir = Path.Combine(
        Path.GetTempPath(), "mmjy-" + Guid.NewGuid().ToString("N"));
    private readonly TestModkitConfig _config;

    public JiangyuLoaderInstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(_gameDir, "Mods"));
        _config = new TestModkitConfig { GameInstallPath = _gameDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, recursive: true); } catch { /* best effort */ }
    }

    // Two releases; only v1.2.3 is stable and carries the loader asset.
    private const string ReleasesJson = @"[
      { ""tag_name"": ""v1.2.3"", ""draft"": false, ""prerelease"": false,
        ""assets"": [
          { ""name"": ""jiangyu-cli-linux-x64.zip"", ""browser_download_url"": ""https://x/cli.zip"" },
          { ""name"": ""Jiangyu.Loader.dll"", ""browser_download_url"": ""https://x/v1.2.3/Jiangyu.Loader.dll"" }
        ] },
      { ""tag_name"": ""v1.2.2"", ""draft"": false, ""prerelease"": false,
        ""assets"": [ { ""name"": ""Jiangyu.Loader.dll"", ""browser_download_url"": ""https://x/v1.2.2/Jiangyu.Loader.dll"" } ] }
    ]";

    [Fact]
    public async Task ListVersions_ReturnsTagsNewestFirst()
    {
        var handler = new StubHttpHandler().Json(ReleasesApi, ReleasesJson);
        var installer = new JiangyuLoaderInstaller(_config, handler);

        var versions = await installer.ListVersionsAsync();

        Assert.Equal(new[] { "v1.2.3", "v1.2.2" }, versions);
    }

    [Fact]
    public async Task InstallLatest_DownloadsLoaderIntoMods()
    {
        var handler = new StubHttpHandler()
            .Json(ReleasesApi, ReleasesJson)
            .Bytes("https://x/v1.2.3/Jiangyu.Loader.dll", new byte[] { 1, 2, 3, 4 });
        var installer = new JiangyuLoaderInstaller(_config, handler);

        var path = await installer.InstallAsync(null);

        Assert.Equal(Path.Combine(_gameDir, "Mods", "Jiangyu.Loader.dll"), path);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(path));
        Assert.NotNull(installer.InstalledLoaderPath);
    }

    [Fact]
    public async Task InstallSpecificVersion_DownloadsThatTagsAsset()
    {
        var handler = new StubHttpHandler()
            .Json(ReleasesApi, ReleasesJson)
            .Bytes("https://x/v1.2.2/Jiangyu.Loader.dll", new byte[] { 9, 9 });
        var installer = new JiangyuLoaderInstaller(_config, handler);

        await installer.InstallAsync("v1.2.2");

        var path = Path.Combine(_gameDir, "Mods", "Jiangyu.Loader.dll");
        Assert.Equal(new byte[] { 9, 9 }, await File.ReadAllBytesAsync(path));
    }
}
