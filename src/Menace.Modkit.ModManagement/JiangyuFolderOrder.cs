using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// The load-order prefix on a Jiangyu mod's folder name (<c>010-WOMENACE</c>).
///
/// The Jiangyu loader walks <c>Mods/</c> in ordinal folder order, so the prefix is
/// zero-padded to a fixed width and ordinal order then matches numeric order. A mod's
/// identity is its manifest <c>name</c>, not its folder, so the folder name is free to
/// carry this: renaming leaves the mod id, its per-save state, and any dependency
/// naming it untouched.
/// </summary>
public static class JiangyuFolderOrder
{
    /// <summary>Width of the numeric prefix. Fixed, so ordinal sorting stays numeric.</summary>
    public const int Digits = 3;

    /// <summary>Highest order a prefix can carry at <see cref="Digits"/> width.</summary>
    public const int MaxOrder = 999;

    private static readonly Regex PrefixPattern =
        new($@"^(\d{{{Digits}}})-", RegexOptions.Compiled);

    /// <summary>Read the order a folder name carries, or false when it has no prefix.</summary>
    public static bool TryParse(string folderName, out int order)
    {
        order = 0;
        if (string.IsNullOrEmpty(folderName))
            return false;

        var match = PrefixPattern.Match(folderName);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out order);
    }

    /// <summary>The folder name without its order prefix. Unprefixed names pass through.</summary>
    public static string StripPrefix(string folderName)
        => string.IsNullOrEmpty(folderName)
            ? folderName
            : PrefixPattern.Replace(folderName, string.Empty, 1);

    /// <summary>The folder name carrying <paramref name="order"/>.</summary>
    public static string Compose(int order, string baseName)
    {
        if (order < 0 || order > MaxOrder)
            throw new ArgumentOutOfRangeException(
                nameof(order), order, $"A folder order prefix holds 0 to {MaxOrder}.");

        return string.Concat(
            order.ToString($"D{Digits}", CultureInfo.InvariantCulture), "-", baseName);
    }
}
