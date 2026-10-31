using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The chips and the list must answer the same question: "All" counts what the search and the filters
/// keep, and <see cref="MountQuery.Apply"/> lists exactly that. A live search for <c>flying:n</c> once
/// showed two chips and an empty grid, so the two runs are pinned against each other here for every
/// operator rather than trusted to agree.
/// </summary>
public sealed class MountQueryAgreementTests
{
    private static readonly MountShelfItem[] Items =
    [
        Item(1, "Alpha", flies: true, tradeable: true, seats: 2, patch: "6.1"),
        Item(2, "Beta", flies: false, tradeable: false, seats: 1, patch: "7.0"),
        Item(3, "Gamma", flies: true, tradeable: false, seats: 1, patch: "2.0"),
        Item(4, "Delta", flies: false, tradeable: true, seats: 1, patch: string.Empty),
    ];

    [Theory]
    [InlineData("flying:y")]
    [InlineData("flying:n")]
    [InlineData("fly:y")]
    [InlineData("tradeable:y")]
    [InlineData("seats:>=2")]
    [InlineData("patch:>=6.1")]
    [InlineData("alpha")]
    [InlineData("")]
    public void TheAllChipCountsWhatTheListLists(string search)
    {
        var query = new MountQuery(search, MountOwnership.All, MountExpansion.All, MountOrder.Cheapest);

        int counted = query.OwnershipCounts(Items)[0];
        int listed = query.Apply(Items, null, Prices()).Count;

        Assert.Equal(counted, listed);
    }

    [Fact]
    public void FlyingNoListsTheGroundMounts()
    {
        var query = new MountQuery("flying:n", MountOwnership.All, MountExpansion.All, MountOrder.Name);
        var names = new List<string>();

        foreach (MountShelfItem item in query.Apply(Items))
        {
            names.Add(item.Name);
        }

        Assert.Equal(new[] { "Beta", "Delta" }, names);
    }

    private static Dictionary<uint, int> Prices() => new() { [1u] = 1000, [2u] = 2000 };

    private static MountShelfItem Item(
        uint id,
        string name,
        bool flies,
        bool tradeable,
        int seats,
        string patch) =>
        new(
            id,
            name,
            0u,
            0u,
            seats,
            patch,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            new MountCommunity(10d, tradeable, false),
            false,
            flies);
}
