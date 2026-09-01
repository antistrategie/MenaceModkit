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

    /// <summary>
    /// A CustomLeader leader pack — a folder under <c>Mods/customleaders/</c> holding a
    /// <c>{leader}_clone.json</c> or <c>{leader}_replace.json</c> plus portrait art, read
    /// by the MenaceCustomLeader framework (itself a raw MelonMod).
    /// </summary>
    Leader,

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

    /// <summary>
    /// The number that sequences this mod within its own loader. For modpacks: the mod-owned
    /// <c>loadOrder</c> from its manifest (patch application order at runtime, last-wins).
    /// For a Jiangyu mod folder: the numeric prefix on the folder name, which is what the
    /// loader's ordinal walk of <c>Mods/</c> sorts on. For a MelonLoader DLL: its
    /// <c>[MelonPriority]</c>, read-only and 0 when the assembly declares none. Null for a
    /// modpack or Jiangyu folder carrying no order at all.
    /// </summary>
    public int? LoadOrder { get; init; }

    /// <summary>
    /// Whether this mod's order lives in its folder name — true only for a Jiangyu mod
    /// folder (one carrying <c>jiangyu.json</c>). A Jiangyu-SDK mod shipped as a loose DLL
    /// is loaded by MelonLoader, not by the Jiangyu loader's folder walk, so it orders by
    /// <c>[MelonPriority]</c> like any other melon.
    /// </summary>
    public bool OrderedByFolderName { get; init; }

    /// <summary>Load order for display — empty for kinds without ordering semantics.</summary>
    public string LoadOrderDisplay => LoadOrder?.ToString() ?? string.Empty;

    /// <summary>
    /// Whether this mod's load order can be changed. Modpacks carry a manifest
    /// <c>loadOrder</c>; Jiangyu mods carry a numeric folder-name prefix, which the Jiangyu
    /// loader's ordinal walk of <c>Mods/</c> sorts on. Raw MelonLoader mods are sequenced by
    /// MelonLoader itself — a topological sort over the dependency attributes compiled into
    /// the assembly, then a stable sort by <c>[MelonPriority]</c> — with ties falling back to
    /// raw <c>Directory.GetFiles</c> discovery order. All of that is either baked into the
    /// assembly or filesystem-dependent, so no manager can reorder them from outside.
    ///
    /// Ordering runs within a kind, never across: a modpack's manifest int and a Jiangyu
    /// folder prefix are separate sequences applied by separate loaders.
    /// </summary>
    public bool SupportsLoadOrder => Kind == ModKind.Modpack || OrderedByFolderName;

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
