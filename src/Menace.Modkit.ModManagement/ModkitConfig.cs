namespace Menace.Modkit.ModManagement;

/// <summary>
/// Ambient accessor for the host-supplied <see cref="IModkitConfig"/>.
///
/// The mod-management services read host paths/flags through <see cref="Current"/>
/// instead of reaching into a UI-app singleton, which is what lets the same code run
/// under both the full Modkit app and the lean standalone Mod Manager. Each host sets
/// <see cref="Current"/> once at startup.
///
/// This holds host <em>configuration</em>, never mod <em>state</em> — the <c>Mods/</c>
/// directory remains the single source of truth for what is installed and enabled.
/// </summary>
public static class ModkitConfig
{
    private static IModkitConfig _current = new NullModkitConfig();

    /// <summary>The active host configuration. Defaults to a null-object until a host sets it.</summary>
    public static IModkitConfig Current
    {
        get => _current;
        set => _current = value ?? new NullModkitConfig();
    }

    /// <summary>
    /// Safe default used before a host has set <see cref="Current"/> (and in tests):
    /// no game located, no dev tools, no component cache.
    /// </summary>
    private sealed class NullModkitConfig : IModkitConfig
    {
        public string? GameInstallPath => null;
        public bool EnableDeveloperTools => false;
        public string? ComponentsCachePath => null;
        public string UpdateChannel => "stable";
        public bool IsBetaChannel => false;
        public bool HasUsedModdingTools => false;
        public string AppVersionFull => "0.0.0";
        public string LoaderVersion => "0.0.0";
    }
}
