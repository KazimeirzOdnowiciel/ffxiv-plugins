using System;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>Which ownership state the shelf lists.</summary>
public enum MountOwnership
{
    All,
    Owned,
    Unowned,

    /// <summary>Only the mounts on the player's own hunting list (see the config's wanted set).</summary>
    Wanted,
}

/// <summary>
/// The rules for the persisted ownership filter. Kept here rather than in <c>Configuration</c> so
/// the migration path is testable without Dalamud (T1): the config reads the value back as an int
/// from disk, and a hand-edited or older file may hold a value this build does not know.
/// </summary>
public static class MountOwnershipRules
{
    /// <summary>
    /// Returns <paramref name="value"/> when it is a known ownership filter, else
    /// <see cref="MountOwnership.All"/>. Adding an enum member automatically makes it persistable.
    /// </summary>
    public static MountOwnership Normalize(MountOwnership value) =>
        Enum.IsDefined(value) ? value : MountOwnership.All;
}

/// <summary>
/// The game expansion a mount belongs to, derived from the major number of its patch.
/// <see cref="Unknown"/> also filters: it selects the mounts whose patch cannot be mapped.
/// </summary>
public enum MountExpansion
{
    All,
    ARealmReborn,
    Heavensward,
    Stormblood,
    Shadowbringers,
    Endwalker,
    Dawntrail,
    Unknown,
}

/// <summary>
/// The one reading of a patch string. The expansion filter and the search both map patches to
/// expansions, and they used to do it with two different parsers - one trimming, one not - so a patch
/// such as <c>" 6.1"</c> filtered as Unknown while a search for "endwalker" still found it.
/// </summary>
public static class MountExpansionRules
{
    /// <summary>
    /// The major version a patch string carries (<c>"6.1"</c> to 6, <c>" 3.55a "</c> to 3), or null when it
    /// has no usable one. Surrounding whitespace and a trailing letter suffix are tolerated.
    /// </summary>
    public static int? MajorOf(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return null;
        }

        ReadOnlySpan<char> trimmed = patch.AsSpan().Trim();
        int dot = trimmed.IndexOf('.');
        ReadOnlySpan<char> majorText = dot < 0 ? trimmed : trimmed[..dot];

        return int.TryParse(majorText, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            ? major
            : null;
    }

    /// <summary>
    /// The version a patch string stands for (<c>"6.1"</c> to 6.1, <c>"3.55a"</c> to 3.55), or null when
    /// it carries no usable one. Trailing letter suffixes sort with their numeric patch instead of after
    /// every parsed one; the ordering and the <c>patch:</c> search term both read this.
    /// </summary>
    public static decimal? PatchValue(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return null;
        }

        ReadOnlySpan<char> span = patch.AsSpan().Trim();
        int end = span.Length;

        while (end > 0 && !char.IsAsciiDigit(span[end - 1]))
        {
            end--;
        }

        if (end == 0)
        {
            return null;
        }

        return decimal.TryParse(
            span[..end],
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out decimal value)
            ? value
            : null;
    }
}

/// <summary>
/// How the shelf draws the filtered mounts: as artwork cards, or as one dense row each. The table is
/// the same list with the facts in columns, for the player who is reading rather than browsing.
/// </summary>
public enum MountViewMode
{
    Grid,
    Table,
}

/// <summary>
/// The persisted view mode, read back as an int from disk: a hand-edited or older config may hold a
/// value this build does not know.
/// </summary>
public static class MountViewModeRules
{
    /// <summary>
    /// Returns <paramref name="value"/> when it is a known view mode, else <see cref="MountViewMode.Grid"/>.
    /// </summary>
    public static MountViewMode Normalize(MountViewMode value) =>
        Enum.IsDefined(value) ? value : MountViewMode.Grid;
}

/// <summary>How the shelf is ordered.</summary>
public enum MountOrder
{
    Name,
    OldestFirst,
    Seats,
    OwnedFirst,

    /// <summary>Newest patch first, so a returning player sees what the latest patch added.</summary>
    NewestFirst,

    /// <summary>
    /// Smallest share of collectors first, so the rarest mount in the list is at the top. Mounts whose
    /// rarity is unrecorded sort last: "unknown" is not "rarest".
    /// </summary>
    RarestFirst,

    /// <summary>
    /// Cheapest first, by the market price the batch read gave each mount. Mounts with no known price
    /// sort last: "unknown" is not "cheap". Appended rather than inserted, so the orders a saved
    /// config already holds keep their meaning.
    /// </summary>
    Cheapest,

    /// <summary>
    /// A shuffled order, so the top of the list is a mount nobody would have picked - "surprise me".
    /// Seeded rather than truly random: the same seed always produces the same sequence, which is what
    /// keeps the list from reshuffling under the pointer every frame (and makes the order testable).
    /// </summary>
    Random,
}
