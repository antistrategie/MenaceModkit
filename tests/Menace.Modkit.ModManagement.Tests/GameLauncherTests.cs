using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Steam-install detection only — the launch itself hands off to Steam/the OS and
/// can't be exercised offline.
/// </summary>
public sealed class GameLauncherTests
{
    [Theory]
    [InlineData("/home/justin/.steam/steam/steamapps/common/Menace")]
    [InlineData("/mnt/games/SteamLibrary/steamapps/common/Menace")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Menace")]
    [InlineData(@"D:\SteamLibrary\STEAMAPPS\common\Menace")]
    public void IsSteamInstall_TrueForSteamLibraryPaths(string path)
        => Assert.True(GameLauncher.IsSteamInstall(path));

    [Theory]
    [InlineData("/opt/games/Menace")]
    [InlineData(@"C:\Games\Menace")]
    // "steamapps" must be a whole path segment, not a substring of one.
    [InlineData("/data/notsteamapps/Menace")]
    public void IsSteamInstall_FalseForNonSteamPaths(string path)
        => Assert.False(GameLauncher.IsSteamInstall(path));
}
