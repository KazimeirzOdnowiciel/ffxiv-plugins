using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// The shelf's current filter and order. <see cref="Apply"/> is pure: it returns a new list and
/// never mutates the caller's, so the wiring layer can keep the full shelf around.
/// </summary>
public sealed record MountQuery(string? Search, MountOwnership Ownership, MountExpansion Expansion, MountOrder Order)
{
    /// <summary>
    /// The source category to keep (<c>Trial</c>, <c>PvP</c>, <c>Event</c>, ...), or null/empty for any.
    /// Matched against every source line's category, case-insensitively.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Every source category on the shelf, distinct and sorted, for the filter's labels. The order is
    /// ordinal-ignore-case so the combo reads alphabetically regardless of dataset order.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static string[] SourceTypes(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MountShelfItem item in items)
        {
            foreach (MountSourceLine source in item.Sources)
            {
                if (!string.IsNullOrWhiteSpace(source.Category))
                {
                    seen.Add(source.Category);
                }
            }
        }

        var types = new List<string>(seen);
        types.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. types];
    }

    /// <summary>
    /// How many mounts each ownership filter would show, in <see cref="MountOwnership"/> order
    /// (<c>All</c>, <c>Owned</c>, <c>Unowned</c>, <c>Wanted</c>). Search and expansion are applied;
    /// ownership is what is being counted.
    /// </summary>
    /// <param name="items">The whole shelf.</param>
    /// <param name="wanted">The hunting list, used for the <see cref="MountOwnership.Wanted"/> count.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public int[] OwnershipCounts(IReadOnlyList<MountShelfItem> items, IReadOnlySet<uint>? wanted = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var counts = new int[4];
        MountSearchQuery search = MountSearchQuery.Parse(Search);

        foreach (MountShelfItem item in items)
        {
            if (!MatchesExpansion(item) || !search.Matches(item) || !MatchesSource(item))
            {
                continue;
            }

            counts[0]++;

            if (item.Owned)
            {
                counts[1]++;
            }
            else
            {
                counts[2]++;
            }

            if (wanted is not null && wanted.Contains(item.Id))
            {
                counts[3]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Filters <paramref name="items"/> by ownership, expansion and the case-insensitive search
    /// over name, description and every source detail, then orders the result. Every order is a
    /// total order: equal sort keys fall back to the name and finally the mount id, so the same
    /// input always produces the same sequence.
    /// </summary>
    /// <param name="items">The whole shelf, in sheet order.</param>
    /// <param name="shuffleSeed">
    /// The seed <see cref="MountOrder.Random"/> ranks the list with, read only by that order. The caller
    /// picks a new one each time the player chooses "Random" again, so the same seed can be held for as
    /// long as the list is on screen.
    /// </param>
    /// <param name="prices">
    /// Cheapest known market price per mount id, read only when <see cref="Order"/> is
    /// <see cref="MountOrder.Cheapest"/>. A null map or a missing id is "price unknown", which sorts
    /// last rather than first.
    /// </param>
    /// <param name="wanted">
    /// The player's hunting list, read only when <see cref="Ownership"/> is
    /// <see cref="MountOwnership.Wanted"/>. Optional so existing callers and tests keep compiling;
    /// a null or empty set with that filter selects nothing. Ids in the set that are not on the
    /// shelf are ignored.
    /// </param>
    public IReadOnlyList<MountShelfItem> Apply(
        IReadOnlyList<MountShelfItem> items,
        IReadOnlySet<uint>? wanted = null,
        IReadOnlyDictionary<uint, int>? prices = null,
        int shuffleSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        var result = new List<MountShelfItem>(items.Count);
        MountSearchQuery search = MountSearchQuery.Parse(Search);

        foreach (MountShelfItem item in items)
        {
            if (MatchesOwnership(item, wanted) && MatchesExpansion(item) && search.Matches(item)
                && MatchesSource(item))
            {
                result.Add(item);
            }
        }

        result.Sort(Order switch
        {
            MountOrder.Cheapest when prices is not null => (left, right) => CompareCheapest(left, right, prices),
            MountOrder.Random => (left, right) => CompareRandom(left, right, shuffleSeed),
            _ => ComparisonFor(Order),
        });

        return result;
    }

    internal static MountExpansion ExpansionOf(string? patch)
    {
        return MountExpansionRules.MajorOf(patch) is { } major
            ? major switch
            {
                2 => MountExpansion.ARealmReborn,
                3 => MountExpansion.Heavensward,
                4 => MountExpansion.Stormblood,
                5 => MountExpansion.Shadowbringers,
                6 => MountExpansion.Endwalker,
                7 => MountExpansion.Dawntrail,
                _ => MountExpansion.Unknown,
            }
            : MountExpansion.Unknown;
    }

    private bool MatchesOwnership(MountShelfItem item, IReadOnlySet<uint>? wanted)
    {
        return Ownership switch
        {
            MountOwnership.Owned => item.Owned,
            MountOwnership.Unowned => !item.Owned,
            MountOwnership.Wanted => wanted is not null && wanted.Contains(item.Id),
            _ => true,
        };
    }

    private bool MatchesExpansion(MountShelfItem item)
    {
        return Expansion == MountExpansion.All || ExpansionOf(item.Patch) == Expansion;
    }

    private bool MatchesSource(MountShelfItem item)
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            return true;
        }

        foreach (MountSourceLine source in item.Sources)
        {
            if (string.Equals(source.Category, Source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Comparison<MountShelfItem> ComparisonFor(MountOrder order)
    {
        return order switch
        {
            MountOrder.OldestFirst => CompareOldestFirst,
            MountOrder.NewestFirst => CompareNewestFirst,
            MountOrder.Seats => CompareSeats,
            MountOrder.OwnedFirst => CompareOwnedFirst,
            MountOrder.RarestFirst => CompareRarestFirst,
            _ => CompareByName,
        };
    }

    private static int CompareByName(MountShelfItem left, MountShelfItem right)
    {
        int byName = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        if (byName != 0)
        {
            return byName;
        }

        byName = StringComparer.Ordinal.Compare(left.Name, right.Name);
        return byName != 0 ? byName : left.Id.CompareTo(right.Id);
    }

    private static int CompareNewestFirst(MountShelfItem left, MountShelfItem right)
    {
        // Reversing ComparePatch wholesale also reversed its "no usable patch sorts last" rule, which
        // put undated mounts at the top of "Newest first". The undated case is handled here.
        bool hasLeft = TryParsePatch(left.Patch, out _);
        bool hasRight = TryParsePatch(right.Patch, out _);

        if (hasLeft != hasRight)
        {
            return hasLeft ? -1 : 1;
        }

        int byPatch = ComparePatch(left.Patch, right.Patch);

        if (byPatch != 0)
        {
            return -byPatch;
        }

        return CompareByName(left, right);
    }

    private static int CompareOldestFirst(MountShelfItem left, MountShelfItem right)
    {
        int byPatch = ComparePatch(left.Patch, right.Patch);
        return byPatch != 0 ? byPatch : CompareByName(left, right);
    }

    private static int CompareSeats(MountShelfItem left, MountShelfItem right)
    {
        int bySeats = right.Seats.CompareTo(left.Seats);
        return bySeats != 0 ? bySeats : CompareByName(left, right);
    }

    private static int CompareOwnedFirst(MountShelfItem left, MountShelfItem right)
    {
        int byOwned = right.Owned.CompareTo(left.Owned);
        return byOwned != 0 ? byOwned : CompareByName(left, right);
    }

    /// <summary>
    /// The rarest first: ascending by the share of collectors who own the mount. This is the one order
    /// where a missing value must not act as a value - reversing a plain compare would put the mounts
    /// whose rarity is unknown at the top, which is the same trap "Newest first" fell into - so those
    /// are parked at the end explicitly.
    /// </summary>
    private static int CompareRarestFirst(MountShelfItem left, MountShelfItem right)
    {
        double? leftOwned = left.Community?.OwnedPercent;
        double? rightOwned = right.Community?.OwnedPercent;

        if (leftOwned.HasValue != rightOwned.HasValue)
        {
            return leftOwned.HasValue ? -1 : 1;
        }

        if (leftOwned.HasValue)
        {
            int byOwned = leftOwned.Value.CompareTo(rightOwned!.Value);

            if (byOwned != 0)
            {
                return byOwned;
            }
        }

        return CompareByName(left, right);
    }

    /// <summary>
    /// Cheapest first, by the price the batch read gave each mount. An unknown price is not a price:
    /// those mounts are parked at the end rather than at the top, the same trap "Newest first" and
    /// "Rarest first" each fell into once, and the same reason both handle it explicitly.
    /// </summary>
    private static int CompareCheapest(
        MountShelfItem left,
        MountShelfItem right,
        IReadOnlyDictionary<uint, int> prices)
    {
        bool hasLeft = prices.TryGetValue(left.Id, out int leftPrice) && leftPrice > 0;
        bool hasRight = prices.TryGetValue(right.Id, out int rightPrice) && rightPrice > 0;

        if (hasLeft != hasRight)
        {
            return hasLeft ? -1 : 1;
        }

        if (hasLeft)
        {
            int byPrice = leftPrice.CompareTo(rightPrice);

            if (byPrice != 0)
            {
                return byPrice;
            }
        }

        return CompareByName(left, right);
    }

    /// <summary>
    /// A shuffled order from a seed: the rank is a 32-bit mix of the mount id and the seed, so a small
    /// change in either gives an unrelated position (and an item keeps its rank for as long as the seed
    /// holds, which is what stops the grid from reshuffling under the pointer).
    /// </summary>
    private static int CompareRandom(MountShelfItem left, MountShelfItem right, int seed)
    {
        int byRank = RankOf(left.Id, seed).CompareTo(RankOf(right.Id, seed));

        return byRank != 0 ? byRank : CompareByName(left, right);
    }

    /// <summary>The shuffle rank of one mount (splitmix-style mixing: cheap, deterministic, and spread).</summary>
    internal static uint RankOf(uint id, int seed)
    {
        var x = id + ((uint)seed * 0x9E3779B9u);

        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;

        return x;
    }

    private static int ComparePatch(string left, string right)
    {
        bool hasLeft = TryParsePatch(left, out decimal leftPatch);
        bool hasRight = TryParsePatch(right, out decimal rightPatch);

        if (hasLeft && hasRight)
        {
            int byValue = leftPatch.CompareTo(rightPatch);
            return byValue != 0 ? byValue : StringComparer.Ordinal.Compare(left, right);
        }

        if (hasLeft)
        {
            return -1;
        }

        return hasRight ? 1 : StringComparer.Ordinal.Compare(left, right);
    }

    private static bool TryParsePatch(string patch, out decimal value)
    {
        if (MountExpansionRules.PatchValue(patch) is { } parsed)
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }
}
