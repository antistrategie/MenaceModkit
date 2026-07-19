namespace Menace.Modkit.ModManagement;

/// <summary>
/// Ambient configuration the mod-management services need, supplied by the host app.
/// Exists so the services are UI-agnostic: it replaces the direct
/// <c>AppSettings.Instance</c> reach-ins that previously tied them to the desktop app.
///
/// Deliberately tiny — grow it only as a moved service genuinely needs a new value.
/// Note: mod state is NOT held here. The <c>Mods/</c> directory is the single source
/// of truth and is scanned live; this interface only supplies host-level paths/flags.
/// </summary>
public interface IModkitConfig
{
    /// <summary>
    /// Absolute path to the game install directory (the folder that contains
    /// <c>Mods/</c>, the <c>*_Data</c> directory and <c>MelonLoader/</c>).
    /// Null or empty when the game has not been located yet.
    /// </summary>
    string? GameInstallPath { get; }

    /// <summary>
    /// Whether developer-only mods and tooling should be included in management
    /// operations (mirrors the app's "developer tools" toggle).
    /// </summary>
    bool EnableDeveloperTools { get; }

    /// <summary>
    /// Absolute path to the downloaded-components cache (used to seed bundled
    /// add-on modpacks). Null when the host has no component cache; consumers
    /// must treat a null/empty value as "no cache" and skip cache-dependent work.
    /// </summary>
    string? ComponentsCachePath { get; }

    /// <summary>Update channel for component/version manifests ("stable" or "beta").</summary>
    string UpdateChannel { get; }

    /// <summary>Convenience: whether <see cref="UpdateChannel"/> is the beta channel.</summary>
    bool IsBetaChannel { get; }

    /// <summary>
    /// Whether the user has previously used the modding tools (gates first-run
    /// behaviour such as forced data extraction).
    /// </summary>
    bool HasUsedModdingTools { get; }

    /// <summary>Full host version string, used to stamp provenance of downloaded components.</summary>
    string AppVersionFull { get; }

    /// <summary>
    /// Version of the bundled loader/modkit component, used to decide whether the
    /// installed "Modkit" component is up to date.
    /// </summary>
    string LoaderVersion { get; }
}
