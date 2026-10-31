using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>How much of one expansion's mounts the player owns.</summary>
/// <param name="Name">The expansion's short name, e.g. <c>Heavensward</c>.</param>
/// <param name="ShortName">The abbreviation shown in the progress strip, e.g. <c>HW</c>.</param>
/// <param name="Owned">Collected mounts in the expansion.</param>
/// <param name="Total">Mounts in the expansion.</param>
public sealed record ExpansionProgress(string Name, string ShortName, int Owned, int Total)
{
    /// <summary>The integer percentage owned, zero when the expansion has no mounts.</summary>
    public int Percent => MountProgress.Percent(Owned, Total);
}

/// <summary>
/// Groups a collection by expansion, in release order, so the window can show where the gaps are
/// instead of one number for the whole game.
/// </summary>
/// <remarks>
/// The expansion comes from the mount's patch string (<c>6.1</c> → Endwalker, <c>2.5</c> → A Realm
/// Reborn). Patch strings that are empty or unparseable fall into an "Other" bucket rather than being
/// dropped, so the totals always add up to the number of mounts given.
/// </remarks>
public static class MountBreakdown
{
    private static readonly (string Name, string ShortName, int Major)[] Expansions =
    [
        ("A Realm Reborn", "ARR", 2),
        ("Heavensward", "HW", 3),
        ("Stormblood", "StB", 4),
        ("Shadowbringers", "ShB", 5),
        ("Endwalker", "EW", 6),
        ("Dawntrail", "DT", 7),
    ];

    /// <summary>
    /// One entry per expansion that has mounts, in release order, plus a trailing "Other" entry when
    /// some mounts carry no usable patch. Empty input yields an empty list.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static IReadOnlyList<ExpansionProgress> ByExpansion(IReadOnlyList<MountShelfItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var owned = new int[Expansions.Length];
        var total = new int[Expansions.Length];
        var otherOwned = 0;
        var otherTotal = 0;

        for (var index = 0; index < items.Count; index++)
        {
            MountShelfItem item = items[index];
            var slot = ExpansionIndex(item.Patch);

            if (slot < 0)
            {
                otherTotal++;

                if (item.Owned)
                {
                    otherOwned++;
                }

                continue;
            }

            total[slot]++;

            if (item.Owned)
            {
                owned[slot]++;
            }
        }

        var result = new List<ExpansionProgress>(Expansions.Length + 1);

        for (var index = 0; index < Expansions.Length; index++)
        {
            if (total[index] == 0)
            {
                continue;
            }

            result.Add(new ExpansionProgress(Expansions[index].Name, Expansions[index].ShortName, owned[index], total[index]));
        }

        if (otherTotal > 0)
        {
            result.Add(new ExpansionProgress("Other", "—", otherOwned, otherTotal));
        }

        return result;
    }

    /// <summary>
    /// The expansion a patch string belongs to, or null when it carries no usable major version.
    /// Exposed so the search can match "endwalker" or "7.0" without duplicating the mapping.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="patch"/> is null.</exception>
    public static ExpansionProgress? ExpansionForPatch(string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var slot = ExpansionIndex(patch);
        return slot < 0
            ? null
            : new ExpansionProgress(Expansions[slot].Name, Expansions[slot].ShortName, 0, 0);
    }

    /// <summary>
    /// The expansion slot for a patch string, or -1 when it carries no usable major version.
    /// </summary>
    private static int ExpansionIndex(string patch)
    {
        // The same parser the filter uses (MountExpansionRules), not a second copy of it.
        if (MountExpansionRules.MajorOf(patch) is not { } major)
        {
            return -1;
        }

        for (var index = 0; index < Expansions.Length; index++)
        {
            if (Expansions[index].Major == major)
            {
                return index;
            }
        }

        return -1;
    }
}
