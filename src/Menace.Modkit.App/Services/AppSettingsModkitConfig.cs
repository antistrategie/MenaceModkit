using Menace.Modkit.ModManagement;

namespace Menace.Modkit.App.Services;

/// <summary>
/// Desktop-app implementation of <see cref="IModkitConfig"/>. Bridges the
/// mod-management library to the app's live <see cref="AppSettings"/> and
/// <see cref="ComponentManager"/> so the moved services keep the exact behaviour
/// they had before extraction. Registered once at startup via
/// <c>ModkitConfig.Current = new AppSettingsModkitConfig()</c>.
/// </summary>
public sealed class AppSettingsModkitConfig : IModkitConfig
{
    public string? GameInstallPath => AppSettings.Instance.GameInstallPath;

    public bool EnableDeveloperTools => AppSettings.Instance.EnableDeveloperTools;

    public string? ComponentsCachePath => ComponentManager.Instance.ComponentsCachePath;

    public string UpdateChannel => AppSettings.Instance.UpdateChannel;

    public bool IsBetaChannel => AppSettings.Instance.IsBetaChannel;

    public bool HasUsedModdingTools => AppSettings.Instance.HasUsedModdingTools;

    public string AppVersionFull => ModkitVersion.AppFull;

    public string LoaderVersion => ModkitVersion.MelonVersion;
}
