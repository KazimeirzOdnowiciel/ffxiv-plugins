using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>The counts behind the ownership chips.</summary>
public sealed class OwnershipCountTests
{
    [Fact]
    public void CountsEachOwnershipInChipOrder()
    {
        var wanted = new HashSet<uint> { 2u, 3u };
        var items = new List<MountShelfItem>
        {
            Item(1u, true),
            Item(2u, false),
            Item(3u, false),
            Item(4u, false),
        };

        var counts = Query().OwnershipCounts(items, wanted);

        Assert.Equal(new[] { 4, 1, 3, 2 }, counts);
    }

    [Fact]
    public void CountsRespectSearchAndExpansion()
    {
        var items = new List<MountShelfItem>
        {
            Item(1u, true, "Alpha", "6.1"),
            Item(2u, false, "Beta", "6.1"),
            Item(3u, false, "Alpha", "2.5"),
        };

        var counts = new MountQuery("alpha", MountOwnership.All, MountExpansion.Endwalker, MountOrder.Name)
            .OwnershipCounts(items);

        Assert.Equal(1, counts[0]);
        Assert.Equal(1, counts[1]);
        Assert.Equal(0, counts[2]);
        Assert.Equal(0, counts[3]);
    }

    [Fact]
    public void NullWantedCountsZero()
    {
        var counts = Query().OwnershipCounts([Item(1u, false)]);

        Assert.Equal(0, counts[3]);
    }

    private static MountQuery Query() =>
        new(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name);

    private static MountShelfItem Item(uint id, bool owned, string name = "Mount", string patch = "6.0") =>
        new(id, name, 0u, 0u, 1, patch, owned, string.Empty, string.Empty, string.Empty, []);
}
