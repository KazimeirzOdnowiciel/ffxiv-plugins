using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// The rarest mount that is still missing: the one number a collector wants out of a header.
/// </summary>
/// <param name="Id">The mount's id, so a caller can select the row it names.</param>
/// <param name="Name">The mount's display name, as the dataset writes it.</param>
/// <param name="OwnedPercent">The share of collectors who own it, exactly as the dataset records it.</param>
public readonly record struct RarestMissing(uint Id, string Name, double OwnedPercent);

/// <summary>
/// The insight the header carries: which mount the player is least likely to meet in the wild, among
/// the ones they do not have.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1). It reads the same <see cref="MountCommunity"/> the table's Owners column
/// prints, and it applies the same rule that the "Rarest first" order applies to an unrecorded share:
/// unknown is not rare. A mount whose share the dataset does not record can neither win this line nor
/// be described by it, because "the rarest mount you are missing" is a claim about a measurement.
/// </para>
/// <para>
/// Ties are settled by name, in the same collation the orders fall back to, so the sentence is stable:
/// two mounts at 0.4% must not swap places between frames.
/// </para>
/// </remarks>
public static class MountRarityInsight
{
    /// <summary>
    /// The unowned mount with the smallest recorded share, or null when there is nothing to name -
    /// every mount owned, no mounts at all, or no unowned mount whose rarity is recorded. The caller
    /// draws nothing for null rather than a sentence with a hole in it.
    /// </summary>
    /// <param name="items">The whole collection, not the filtered list: this is a fact about the
    /// collection, like the header's own counters, and a filter must not change who the rarest is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static RarestMissing? Find(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        RarestMissing? found = null;

        for (var index = 0; index < items.Count; index++)
        {
            MountShelfItem item = items[index];

            if (item.Owned || item.Community?.OwnedPercent is not { } percent || double.IsNaN(percent))
            {
                continue;
            }

            if (found is { } best
                && (percent > best.OwnedPercent
                    || (percent == best.OwnedPercent && CompareByName(item, best) >= 0)))
            {
                continue;
            }

            found = new RarestMissing(item.Id, item.Name, percent);
        }

        return found;
    }

    /// <summary>
    /// "Rarest missing: Astrope (0.5%)" - the sentence the header draws beside its counters, or empty
    /// when there is nothing to name.
    /// </summary>
    /// <param name="missing">The mount to describe, or null.</param>
    public static string Line(RarestMissing? missing) =>
        missing is { } rarest
            ? Loc.F("header.rarest", rarest.Name, MountRarity.ShareText(rarest.OwnedPercent))
            : string.Empty;

    /// <summary>
    /// The same fact without the leading word ("Rarest: Astrope (0.5%)"), for a header too narrow for
    /// the full sentence: dropping a word reads better than an ellipsis in the middle of a name.
    /// </summary>
    /// <param name="missing">The mount to describe, or null.</param>
    public static string CompactLine(RarestMissing? missing) =>
        missing is { } rarest
            ? Loc.F("header.rarest.compact", rarest.Name, MountRarity.ShareText(rarest.OwnedPercent))
            : string.Empty;

    /// <summary>
    /// Whether <paramref name="item"/> sorts before <paramref name="best"/> on a tie: the name collation
    /// <see cref="MountQuery"/> falls back to, then the id, so the answer never depends on list order.
    /// </summary>
    private static int CompareByName(MountShelfItem item, RarestMissing best)
    {
        int byName = StringComparer.OrdinalIgnoreCase.Compare(item.Name, best.Name);

        if (byName != 0)
        {
            return byName;
        }

        byName = StringComparer.Ordinal.Compare(item.Name, best.Name);

        return byName != 0 ? byName : item.Id.CompareTo(best.Id);
    }
}
