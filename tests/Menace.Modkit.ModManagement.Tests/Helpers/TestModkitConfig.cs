namespace Menace.Modkit.ModManagement.Tests.Helpers;

/// <summary>In-memory <see cref="IModkitConfig"/> for tests — points at a temp game dir.</summary>
internal sealed class TestModkitConfig : IModkitConfig
{
    public string? GameInstallPath { get; init; }
    public bool EnableDeveloperTools { get; init; } = true;
    public string? ComponentsCachePath { get; init; }
    public string UpdateChannel => "stable";
    public bool IsBetaChannel => false;
    public bool HasUsedModdingTools => false;
    public string AppVersionFull => "test";
    public string LoaderVersion => "test";
}
