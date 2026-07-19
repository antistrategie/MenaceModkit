using System;
using System.Collections.Generic;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Metadata read statically from a mod DLL (no assembly load), used to present and
/// classify raw MelonLoader / Jiangyu mods that carry no <c>modpack.json</c>.
/// </summary>
public sealed record MelonModInfo
{
    /// <summary>Mod name from <c>[MelonInfo]</c>, or null if the attribute is absent.</summary>
    public string? Name { get; init; }

    /// <summary>Version from <c>[MelonInfo]</c>, or null.</summary>
    public string? Version { get; init; }

    /// <summary>Author from <c>[MelonInfo]</c>, or null.</summary>
    public string? Author { get; init; }

    /// <summary>Whether an assembly-level <c>[MelonInfo]</c>/<c>[MelonModInfo]</c> attribute was found.</summary>
    public bool HasMelonInfo { get; init; }

    /// <summary>Whether the assembly references MelonLoader (a strong signal it is a MelonMod).</summary>
    public bool ReferencesMelonLoader { get; init; }

    /// <summary>Whether the assembly references the Jiangyu SDK/loader.</summary>
    public bool IsJiangyu { get; init; }

    /// <summary>Simple names of every assembly this DLL references.</summary>
    public IReadOnlyList<string> ReferencedAssemblies { get; init; } = Array.Empty<string>();

    /// <summary>True when this looks like a MelonLoader mod (has [MelonInfo] or references MelonLoader).</summary>
    public bool IsMelonMod => HasMelonInfo || ReferencesMelonLoader;
}
