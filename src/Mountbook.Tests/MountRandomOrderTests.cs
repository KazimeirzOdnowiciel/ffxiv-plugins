using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The <c>Random</c> order - "surprise me". The point of it is that the seed decides everything: the same
/// seed gives the same sequence (the list must not reshuffle while it is on screen, or under the pointer),
/// and a different seed gives a different one (picking it again must actually surprise).
/// </summary>
public sealed class MountRandomOrderTests
{
    [Fact]
    public void TheSameSeedAlwaysGivesTheSameOrder()
    {
        IReadOnlyList<MountShelfItem> items = Shelf();

        Assert.Equal(Names(Apply(items, 1234)), Names(Apply(items, 1234)));
    }

    [Fact]
    public void ANewSeedGivesADifferentOrder()
    {
        IReadOnlyList<MountShelfItem> items = Shelf();

        // Not a guarantee for one pair of arbitrary seeds, so this walks a few: at least one of them has to
        // differ, which is the property "picking Random again shows something else" rests on.
        string[] baseline = Names(Apply(items, 7));
        var differed = false;

        foreach (int seed in new[] { 8, 9, 10, 424242, -1 })
        {
            if (Names(Apply(items, seed)) is var other && !Same(baseline, other))
            {
                differed = true;
                break;
            }
        }

        Assert.True(differed, "Every seed produced the same sequence; the shuffle is not seeded.");
    }

    [Fact]
    public void NothingIsLostOrDuplicated()
    {
        IReadOnlyList<MountShelfItem> items = Shelf();
        IReadOnlyList<MountShelfItem> shuffled = Apply(items, 99);

        Assert.Equal(items.Count, shuffled.Count);

        var seen = new HashSet<uint>();

        foreach (MountShelfItem item in shuffled)
        {
            Assert.True(seen.Add(item.Id), $"Mount {item.Id} appeared twice.");
        }
    }

    [Fact]
    public void TheOrderStillFiltersWhatTheListHolds()
    {
        // Random orders the result; it must not resurrect mounts the filter excluded. The wanted chip is
        // the one filter that reads a set the caller passes, so it is what this can drive from a test.
        var wanted = new HashSet<uint> { 2u, 4u };
        var query = new MountQuery(string.Empty, MountOwnership.Wanted, MountExpansion.All, MountOrder.Random);

        var names = new List<string>();

        foreach (MountShelfItem item in query.Apply(Shelf(), wanted, null, 3))
        {
            names.Add(item.Name);
        }

        Assert.Equal(2, names.Count);
        Assert.Contains("Beta", names);
        Assert.Contains("Delta", names);
    }

    [Fact]
    public void ARankIsStablePerIdAndSeed() =>
        // The comparator is built on this: an unstable rank would make Sort produce a different list on
        // every frame with the same seed.
        Assert.Equal(MountQuery.RankOf(42u, 5), MountQuery.RankOf(42u, 5));

    private static IReadOnlyList<MountShelfItem> Apply(IReadOnlyList<MountShelfItem> items, int seed) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Random)
            .Apply(items, null, null, seed);

    private static string[] Names(IReadOnlyList<MountShelfItem> items)
    {
        var names = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            names[index] = items[index].Name;
        }

        return names;
    }

    private static bool Same(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index], right[index], System.StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<MountShelfItem> Shelf()
    {
        var items = new List<MountShelfItem>(8);

        for (uint id = 1; id <= 8; id++)
        {
            items.Add(new MountShelfItem(
                id,
                id switch
                {
                    1 => "Alpha",
                    2 => "Beta",
                    3 => "Gamma",
                    4 => "Delta",
                    5 => "Epsilon",
                    6 => "Zeta",
                    7 => "Eta",
                    _ => "Theta",
                },
                0u,
                0u,
                1,
                "7.0",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                []));
        }

        return items;
    }
}
