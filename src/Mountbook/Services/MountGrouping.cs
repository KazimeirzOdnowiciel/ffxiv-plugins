using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>How the card grid gathers its mounts into labelled sections.</summary>
public enum MountGrouping
{
    /// <summary>One flat grid, in the order the list already has.</summary>
    None,

    /// <summary>One section per expansion, in release order.</summary>
    Expansion,

    /// <summary>One section per patch, newest first.</summary>
    Patch,

    /// <summary>One section per acquire method, alphabetically.</summary>
    Source,
}

/// <summary>
/// The persisted grouping, read back as an int from disk: a hand-edited or older config may hold a value
/// this build does not know.
/// </summary>
public static class MountGroupingRules
{
    /// <summary>Returns <paramref name="value"/> when it is a known grouping, else none.</summary>
    /// <param name="value">The value read from the config.</param>
    public static MountGrouping Normalize(MountGrouping value) =>
        Enum.IsDefined(value) ? value : MountGrouping.None;
}

/// <summary>One labelled run of mounts: the heading the grid draws and the cards under it.</summary>
/// <param name="Title">The heading text, e.g. <c>Dawntrail</c>, <c>7.05</c> or <c>Trial</c>.</param>
/// <param name="Items">The mounts in this section, in the order the list already had.</param>
public sealed record MountGroup(string Title, IReadOnlyList<MountShelfItem> Items);

/// <summary>
/// Splits an ordered list into the sections the grid draws.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1), and it never sorts the mounts inside a section: the query already decided
/// that order, and a second ordering is how two views start disagreeing about what "cheapest first" means.
/// </para>
/// <para>
/// The section order is fixed per grouping rather than "first appearance", because the list order is the
/// player's choice (newest, cheapest, rarest...) and sections that reorder themselves when the sort changes
/// are unreadable. Expansion follows release order, patch follows newest first, and source follows the obtain
/// taxonomy - the largest group first, the same order the filter's own list uses - each with the unrecorded
/// case last, the way every other order in this plugin treats a missing value.
/// </para>
/// </remarks>
public static class MountGroups
{
    /// <summary>The heading for mounts whose patch the dataset does not record.</summary>
    public const string NoPatch = "No patch recorded";

    /// <summary>The heading for mounts with no obtain data.</summary>
    public const string NoSource = "No source recorded";

    /// <summary>
    /// Groups <paramref name="items"/> for <paramref name="grouping"/>. With
    /// <see cref="MountGrouping.None"/> the answer is one group with an empty title, so the caller has one
    /// shape to draw either way.
    /// </summary>
    /// <param name="items">The filtered, ordered list.</param>
    /// <param name="grouping">How to split it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static IReadOnlyList<MountGroup> Build(IReadOnlyList<MountShelfItem> items, MountGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (grouping == MountGrouping.None || items.Count == 0)
        {
            return [new MountGroup(string.Empty, items)];
        }

        // One bucket per heading, then the headings sorted by the grouping's own rule. A dictionary keeps
        // the first key's insertion cost down and lets the sort decide the visible order.
        var buckets = new Dictionary<string, List<MountShelfItem>>(StringComparer.Ordinal);

        foreach (MountShelfItem item in items)
        {
            string title = TitleOf(item, grouping);

            if (!buckets.TryGetValue(title, out List<MountShelfItem>? bucket))
            {
                bucket = [];
                buckets[title] = bucket;
            }

            bucket.Add(item);
        }

        var titles = new List<string>(buckets.Keys);
        titles.Sort((left, right) => CompareTitles(left, right, grouping));

        var groups = new List<MountGroup>(titles.Count);

        foreach (string title in titles)
        {
            groups.Add(new MountGroup(title, buckets[title]));
        }

        return groups;
    }

    private static string TitleOf(MountShelfItem item, MountGrouping grouping) => grouping switch
    {
        MountGrouping.Expansion => MountQuery.ExpansionOf(item.Patch) switch
        {
            MountExpansion.ARealmReborn => "A Realm Reborn",
            MountExpansion.Heavensward => "Heavensward",
            MountExpansion.Stormblood => "Stormblood",
            MountExpansion.Shadowbringers => "Shadowbringers",
            MountExpansion.Endwalker => "Endwalker",
            MountExpansion.Dawntrail => "Dawntrail",
            _ => "Expansion unknown",
        },
        MountGrouping.Patch => string.IsNullOrWhiteSpace(item.Patch) ? NoPatch : item.Patch.Trim(),
        _ => PrimarySource(item),
    };

    /// <summary>
    /// The heading a mount goes under when the list is grouped by source: the type of its first obtain line.
    /// The site lists a mount's primary source first, so one earned by a quest that also grants an achievement
    /// is filed under the quest - a heading per type would draw the same card in two sections, and it is a
    /// partition that makes the headings' counts add up to the list.
    /// </summary>
    private static string PrimarySource(MountShelfItem item) =>
        item.Sources.Count > 0 && !item.Sources[0].Kind.IsAny ? item.Sources[0].Kind.Name : NoSource;

    private static int CompareTitles(string left, string right, MountGrouping grouping) => grouping switch
    {
        MountGrouping.Expansion => ExpansionRank(left).CompareTo(ExpansionRank(right)) is var byRank and not 0
            ? byRank
            : StringComparer.Ordinal.Compare(left, right),
        MountGrouping.Patch => ComparePatchTitles(left, right),
        _ => CompareSourceTitles(left, right),
    };

    private static int ExpansionRank(string title) => title switch
    {
        "A Realm Reborn" => 2,
        "Heavensward" => 3,
        "Stormblood" => 4,
        "Shadowbringers" => 5,
        "Endwalker" => 6,
        "Dawntrail" => 7,
        _ => int.MaxValue,
    };

    private static int ComparePatchTitles(string left, string right)
    {
        // Newest first, and the unrecorded heading last: a patch nobody wrote down is not the newest one.
        bool hasLeft = MountExpansionRules.PatchValue(left) is not null;
        bool hasRight = MountExpansionRules.PatchValue(right) is not null;

        if (hasLeft != hasRight)
        {
            return hasLeft ? -1 : 1;
        }

        if (hasLeft && MountExpansionRules.PatchValue(left) is { } leftValue
            && MountExpansionRules.PatchValue(right) is { } rightValue)
        {
            int byValue = rightValue.CompareTo(leftValue);

            if (byValue != 0)
            {
                return byValue;
            }
        }

        return StringComparer.Ordinal.Compare(left, right);
    }

    /// <summary>
    /// Taxonomy order, not the alphabet: the headings of a source-grouped grid read like the filter's own list,
    /// so the biggest bucket - Premium, in the game's own classification - is where a player looks first
    /// instead of buried between "PvP" and "Quest". A type this build does not know ranks after every type it
    /// does, and "No source recorded" last of all: a mount with no obtain data belongs to no section.
    /// </summary>
    private static int CompareSourceTitles(string left, string right)
    {
        (bool leftNoSource, int leftRank, string leftName) = SourceOrder(left);
        (bool rightNoSource, int rightRank, string rightName) = SourceOrder(right);

        if (leftNoSource != rightNoSource)
        {
            return leftNoSource ? 1 : -1;
        }

        int byRank = leftRank.CompareTo(rightRank);

        if (byRank != 0)
        {
            return byRank;
        }

        int byName = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
        return byName != 0 ? byName : StringComparer.Ordinal.Compare(leftName, rightName);
    }

    /// <summary>Where one source heading belongs: the missing-data heading last, then the type's own rank.</summary>
    private static (bool NoSource, int Rank, string Name) SourceOrder(string title)
    {
        if (string.Equals(title, NoSource, StringComparison.Ordinal))
        {
            return (true, MountSourceKinds.Unranked, title);
        }

        MountSourceKind kind = MountSourceKinds.Parse(title);
        return (false, kind.Rank, kind.Name);
    }
}
