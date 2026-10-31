using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// Filtering, search and ordering are pure decisions the window delegates to this type, so they
/// are pinned here instead of in the game; the golden only proves the resulting list was drawn.
/// </summary>
public sealed class MountQueryTests
{
    [Fact]
    public void ApplyNeverMutatesTheCallersList()
    {
        // Apply is documented as returning a new list and leaving the caller's untouched, and the window
        // passes the live catalog list - which the breakdown, the texture cache and FindSelected all read.
        // An in-place Sort here would corrupt every one of them, silently.
        MountShelfItem[] input = [Item(2, "Zebra"), Item(1, "Alpha")];
        MountShelfItem[] before = [.. input];

        var result = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.Name).Apply(input);

        Assert.Equal(before, input);
        Assert.Equal(["Alpha", "Zebra"], result.Select(item => item.Name));
        Assert.NotSame(input, result);
    }

    [Fact]
    public void NullEmptyOrWhitespaceSearchMatchesEverything()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha"),
            Item(2, "Beta", description: "A floating salad"),
            Item(3, "Gamma", sourceDetail: "SDS Fenrir"),
        ];

        string?[] searches = [null, string.Empty, "   "];

        foreach (string? search in searches)
        {
            var query = new MountQuery(search, MountOwnership.All, MountExpansion.All, MountOrder.Name);

            Assert.Equal(items.Length, query.Apply(items).Count);
        }
    }

    [Fact]
    public void SearchMatchesTheNameCaseInsensitively()
    {
        MountShelfItem[] items =
        [
            Item(1, "Magitek Scorpion"),
            Item(2, "Cloud Mallow"),
        ];

        var query = new MountQuery("scorpION", MountOwnership.All, MountExpansion.All, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items));
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void SearchMatchesTheDescription()
    {
        MountShelfItem[] items =
        [
            Item(1, "Cloud Mallow", description: "A floating salad"),
            Item(2, "Cloud Mallow Too", description: "A floating island"),
        ];

        var query = new MountQuery("salad", MountOwnership.All, MountExpansion.All, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items));
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void SearchMatchesEverySourceDetail()
    {
        MountShelfItem[] items =
        [
            Item(1, "Fenrir", sourceDetail: "SDS Fenrir"),
            Item(2, "Cloud Mallow", sourceDetail: "The Firmament"),
        ];

        var query = new MountQuery("sds", MountOwnership.All, MountExpansion.All, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items));
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void SearchThatMatchesNothingReturnsAnEmptyList()
    {
        MountShelfItem[] items = [Item(1, "Fenrir"), Item(2, "Cloud Mallow")];

        var query = new MountQuery("no such mount", MountOwnership.All, MountExpansion.All, MountOrder.Name);

        Assert.Empty(query.Apply(items));
    }

    [Theory]
    [InlineData("2.0", MountExpansion.ARealmReborn)]
    [InlineData("3.05", MountExpansion.Heavensward)]
    [InlineData("4.06", MountExpansion.Stormblood)]
    [InlineData("5.0", MountExpansion.Shadowbringers)]
    [InlineData("6.5", MountExpansion.Endwalker)]
    [InlineData("7.57", MountExpansion.Dawntrail)]
    [InlineData("", MountExpansion.Unknown)]
    [InlineData("garbage", MountExpansion.Unknown)]
    public void ExpansionComesFromThePatchMajor(string patch, MountExpansion expected)
    {
        string otherPatch = expected == MountExpansion.Unknown ? "2.0" : string.Empty;
        MountShelfItem[] items = [Item(1, "Target", patch: patch), Item(2, "Other", patch: otherPatch)];

        var query = new MountQuery(null, MountOwnership.All, expected, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items));
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void AllExpansionsSelectEveryPatch()
    {
        MountShelfItem[] items =
        [
            Item(1, "Old", patch: "2.0"),
            Item(2, "Unknown", patch: "garbage"),
            Item(3, "New", patch: "7.57"),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.Name);

        Assert.Equal(items.Length, query.Apply(items).Count);
    }

    [Fact]
    public void OwnershipAllKeepsEveryMount()
    {
        MountShelfItem[] items =
        [
            Item(1, "Collected", owned: true),
            Item(2, "Missing", owned: false),
            Item(3, "AlsoMissing", owned: false),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.Name);

        Assert.Equal(items.Length, query.Apply(items).Count);
    }

    [Fact]
    public void OwnershipOwnedKeepsOnlyCollectedMounts()
    {
        MountShelfItem[] items =
        [
            Item(1, "Collected", owned: true),
            Item(2, "Missing", owned: false),
            Item(3, "AlsoMissing", owned: false),
        ];

        var query = new MountQuery(null, MountOwnership.Owned, MountExpansion.All, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items));
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void OwnershipUnownedKeepsOnlyMissingMounts()
    {
        MountShelfItem[] items =
        [
            Item(1, "Collected", owned: true),
            Item(2, "Missing", owned: false),
            Item(3, "AlsoMissing", owned: false),
        ];

        var query = new MountQuery(null, MountOwnership.Unowned, MountExpansion.All, MountOrder.Name);

        // Name order is ordinal-ignore-case, so "AlsoMissing" comes before "Missing" - the
        // expectation is the ordered ids, not the order they were handed in.
        uint[] expected = [3, 2];
        Assert.Equal<uint>(expected, Ids(query.Apply(items).ToArray()));
    }

    [Fact]
    public void NameOrderIsOrdinalIgnoreCase()
    {
        MountShelfItem[] items =
        [
            Item(1, "beta"),
            Item(2, "Alpha"),
            Item(3, "charlie"),
            Item(4, "Delta"),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.Name);

        uint[] expected = [2, 1, 3, 4];
        Assert.Equal<uint>(expected, Ids(query.Apply(items).ToArray()));
    }

    [Fact]
    public void OldestFirstOrdersByPatchThenName()
    {
        MountShelfItem[] items =
        [
            Item(1, "Zeta", patch: "7.57"),
            Item(2, "Beta", patch: "2.0"),
            Item(3, "Alpha", patch: "2.0"),
            Item(4, "Gamma", patch: "3.05"),
            Item(5, "Suffix", patch: "3.55a"),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.OldestFirst);

        uint[] expected = [3, 2, 4, 5, 1];
        Assert.Equal<uint>(expected, Ids(query.Apply(items).ToArray()));
    }

    [Fact]
    public void SeatsOrderIsDescendingThenName()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", seats: 1),
            Item(2, "Beta", seats: 4),
            Item(3, "Gamma", seats: 2),
            Item(4, "Delta", seats: 4),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.Seats);

        uint[] expected = [2, 4, 3, 1];
        Assert.Equal<uint>(expected, Ids(query.Apply(items).ToArray()));
    }

    [Fact]
    public void OwnedFirstOrdersByOwnershipThenName()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", owned: false),
            Item(2, "Beta", owned: true),
            Item(3, "Gamma", owned: true),
            Item(4, "Delta", owned: false),
        ];

        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, MountOrder.OwnedFirst);

        uint[] expected = [2, 3, 1, 4];
        Assert.Equal<uint>(expected, Ids(query.Apply(items).ToArray()));
    }

    [Theory]
    [InlineData(MountOrder.Name)]
    [InlineData(MountOrder.OldestFirst)]
    [InlineData(MountOrder.Seats)]
    [InlineData(MountOrder.OwnedFirst)]
    public void EveryOrderIsATotalOrderThatIgnoresInputOrder(MountOrder order)
    {
        MountShelfItem[] items =
        [
            Item(10, "Echo", patch: "7.0", owned: true, seats: 2),
            Item(20, "Echo", patch: "7.0", owned: false, seats: 2),
            Item(30, "Alpha", patch: "2.0", owned: false, seats: 2),
            Item(40, "Zulu", patch: "2.0", owned: true, seats: 5),
        ];
        MountShelfItem[] reversed = items.Reverse().ToArray();
        var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, order);

        uint[] first = Ids(query.Apply(items).ToArray());
        uint[] second = Ids(query.Apply(items).ToArray());
        uint[] fromReversed = Ids(query.Apply(reversed).ToArray());

        Assert.Equal<uint>(first, second);
        Assert.Equal<uint>(first, fromReversed);
    }

    [Fact]
    public void WantedOwnershipKeepsOnlyIdsOnTheHuntingList()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha"),
            Item(2, "Beta"),
            Item(3, "Gamma"),
        ];

        HashSet<uint> wanted = [3, 1];
        var query = new MountQuery(null, MountOwnership.Wanted, MountExpansion.All, MountOrder.Name);

        // The filter narrows, the order still decides the sequence: Alpha before Gamma.
        uint[] expected = [1, 3];
        Assert.Equal<uint>(expected, Ids(query.Apply(items, wanted).ToArray()));
    }

    [Fact]
    public void WantedOwnershipWithNoHuntingListSelectsNothing()
    {
        MountShelfItem[] items = [Item(1, "Alpha"), Item(2, "Beta")];
        var query = new MountQuery(null, MountOwnership.Wanted, MountExpansion.All, MountOrder.Name);

        // The set is optional for source compatibility; omitting it with this filter is empty.
        Assert.Empty(query.Apply(items));
        Assert.Empty(query.Apply(items, new HashSet<uint>()));
    }

    [Fact]
    public void WantedOwnershipIgnoresIdsThatAreNotOnTheShelf()
    {
        MountShelfItem[] items = [Item(1, "Alpha"), Item(2, "Beta")];
        HashSet<uint> wanted = [999, 2];

        var query = new MountQuery(null, MountOwnership.Wanted, MountExpansion.All, MountOrder.Name);

        MountShelfItem result = Assert.Single(query.Apply(items, wanted));
        Assert.Equal(2u, result.Id);
    }

    [Fact]
    public void WantedOwnershipComposesWithSearchExpansionAndOrder()
    {
        MountShelfItem[] items =
        [
            Item(1, "Alpha", patch: "2.0"),
            Item(2, "Beta", patch: "7.0"),
            Item(3, "Gamma", patch: "2.0"),
            Item(4, "Zeta", patch: "2.0"),
        ];

        HashSet<uint> wanted = [1, 2, 3];
        var query = new MountQuery("a", MountOwnership.Wanted, MountExpansion.ARealmReborn, MountOrder.Name);

        // "a" matches Alpha and Gamma; Beta is Endwalker, Zeta is not on the hunting list.
        uint[] expected = [1, 3];
        Assert.Equal<uint>(expected, Ids(query.Apply(items, wanted).ToArray()));
    }

    private static uint[] Ids(MountShelfItem[] items)
    {
        uint[] ids = new uint[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            ids[index] = items[index].Id;
        }

        return ids;
    }

    private static MountShelfItem Item(
        uint id,
        string name,
        string patch = "7.0",
        bool owned = false,
        int seats = 1,
        string description = "",
        string sourceDetail = "")
    {
        MountSourceLine[] lines = [];
        if (sourceDetail.Length > 0)
        {
            lines = [new MountSourceLine("Trial", sourceDetail, string.Empty, 0)];
        }

        return new MountShelfItem(
            id,
            name,
            0,
            0,
            seats,
            patch,
            owned,
            description,
            string.Empty,
            string.Empty,
            lines);
    }
}
