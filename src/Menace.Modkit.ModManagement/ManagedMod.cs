using System;
using System.Collections.Generic;

namespace Menace.Modkit.ModManagement;

/// <summary>The kind of thing a <see cref="ManagedMod"/> is.</summary>
public enum ModKind
{
    /// <summary>A Menace Modkit modpack — a folder containing <c>modpack.json</c>.</summary>
    Modpack,

    /// <summary>A raw MelonLoader mod — a bare <c>.dll</c> in <c>Mods/</c>.</summary>
    MelonMod,

    /// <summary>A MelonLoader mod built against the Jiangyu SDK/loader.</summary>
    Jiangyu,

    /// <summary>Modkit infrastructure (loader/extractor DLLs) — shown but protected from management.</summary>
    Infrastructure,
}

/// <summary>
/// A single mod as discovered in the game's <c>Mods/</c> directory. This is a snapshot
/// of on-disk state, produced fresh by <see cref="ModCatalog"/> on every scan — there is
/// no persisted ledger; <c>Mods/</c> is the single source of truth.
/// </summary>
public sealed class ManagedMod
{
    public required ModKind Kind { get; init; }

    /// <summary>Stable identity within a scan (modpack name or DLL file name).</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string Version { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// For Jiangyu mods: the Jiangyu loader version the mod was compiled against
    /// (the <c>compiledForJiangyu</c> stamp). Null for other kinds.
    /// </summary>
    public string? CompiledForJiangyu { get; init; }

    /// <summary>Version shown in the UI — includes the Jiangyu target for Jiangyu mods.</summary>
    public string VersionDisplay =>
        string.IsNullOrEmpty(CompiledForJiangyu)
            ? Version
            : string.IsNullOrEmpty(Version) ? $"JY {CompiledForJiangyu}" : $"{Version} · JY {CompiledForJiangyu}";

    /// <summary>
    /// Whether the mod is active. For DLLs this reflects the <c>.dll</c> vs
    /// <c>.dll.disabled</c> file name; for modpacks it reflects presence in <c>Mods/</c>.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>Absolute path to the mod's folder (modpack) or file (DLL) in <c>Mods/</c>.</summary>
    public required string Location { get; init; }

    /// <summary>Human-readable warnings surfaced to the user (e.g. known conflicts).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>True for mods the manager must not touch (infrastructure).</summary>
    public bool IsProtected => Kind == ModKind.Infrastructure;

    /// <summary>Whether the user may enable/disable this mod.</summary>
    public bool CanToggle => !IsProtected;
}
