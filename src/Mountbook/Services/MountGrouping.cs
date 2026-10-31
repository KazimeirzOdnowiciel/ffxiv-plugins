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
/// are unreadable. Expansion follows release order, patch follows newest first, and source follows the
/// alphabet - each with the unrecorded case last, the way every other order in this plugin treats a
/// missing value.
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
        _ => item.Sources.Count > 0 && !string.IsNullOrWhiteSpace(item.Sources[0].Category)
            ? item.Sources[0].Category
            : NoSource,
    };

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

    private static int CompareSourceTitles(string left, string right)
    {
        bool hasLeft = !string.Equals(left, NoSource, StringComparison.Ordinal);
        bool hasRight = !string.Equals(right, NoSource, StringComparison.Ordinal);

        if (hasLeft != hasRight)
        {
            return hasLeft ? -1 : 1;
        }

        int byName = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return byName != 0 ? byName : StringComparer.Ordinal.Compare(left, right);
    }
}
