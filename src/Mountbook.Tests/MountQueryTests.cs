using System;
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

    /// <summary>
    /// The caret against the rows it claims to describe, for every order at once - the assertion the Seats bug
    /// was missing. <see cref="SeatsOrderIsDescendingThenName"/> pins the comparator, and MountTableTests pinned
    /// <see cref="MountTable.Descends"/> against its own <see cref="MountTable.DescribedBy"/>, so a hand-kept
    /// direction could pass both while the header drew "ascending" over rows sorted most-seats-first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direction is measured rather than restated: a probe shelf whose four mounts each carry a distinct
    /// value in every column is sorted by the production comparator, and the run between its first and last
    /// keys is what the caret has to claim. The names run A to D while every other key runs D to A, so a
    /// comparator that fell back to the name - an unknown price, say - is caught instead of reading as a
    /// direction.
    /// </para>
    /// <para>
    /// <see cref="MountOrder.Random"/> is the one order with no key of its own: it draws the name column's
    /// caret because it shares ImGui's table id with the name order, so that is what is pinned for it. And the
    /// Cheapest direction is measured with prices, which is the state the caret is honest in: with no price read
    /// the rows fall back to the name and the caret still names the price column, but name order is ascending
    /// like price order, so only the column it names is ahead of the data, not the direction - and the empty
    /// price column is what says so. That is why the header is left alone rather than taught to apologise.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCaretPointsTheWayEveryOrdersRowsRun()
    {
        MountShelfItem[] probe =
        [
            Item(1, "Alpha", patch: "7.0", owned: true, seats: 4, ownedPercent: 10),
            Item(2, "Bravo", patch: "6.0", owned: false, seats: 8, ownedPercent: 0.5),
            Item(3, "Charlie", patch: "5.0", owned: true, seats: 2, ownedPercent: 50),
            Item(4, "Delta", patch: "4.0", owned: false, seats: 1, ownedPercent: 2),
        ];

        Dictionary<uint, int> prices = new()
        {
            [1u] = 300_000,
            [2u] = 1_000_000,
            [3u] = 100_000,
            [4u] = 2_000_000,
        };

        foreach (MountOrder order in Enum.GetValues<MountOrder>())
        {
            TableSort sort = TableSort.At(order);

            if (order == MountOrder.Random)
            {
                // No key of its own, so no direction to measure: the shuffled order draws the name
                // column's caret because it shares ImGui's table id with the name order.
                Assert.Equal(MountTable.NameColumn, sort.Column);
                Assert.False(sort.Descending);
                continue;
            }

            var query = new MountQuery(null, MountOwnership.All, MountExpansion.All, order);
            MountShelfItem[] rows = [.. query.Apply(probe, prices: prices, shuffleSeed: 1)];

            Func<MountShelfItem, IComparable> keyOf = KeyOf(sort.Column, prices);
            int run = keyOf(rows[0]).CompareTo(keyOf(rows[^1]));

            // The probe separates the rows on every column, so a zero here means the key read is wrong,
            // not that the order is in two directions at once.
            Assert.NotEqual(0, run);
            Assert.Equal(run < 0, !sort.Descending);

            // The header draws from the pair the repair accepts: an order reporting its own column as
            // ascending - the state the Seats bug was seen in - has to be repaired exactly when its rows run
            // the other way, and otherwise left alone so the caret is not rewritten every frame.
            Assert.True(MountTable.DescribedBy(order, sort.Column, sort.Descending));
            Assert.Equal(sort, TableSort.Observe(order, sort.Column, descending: false, reported: true, pressed: false));
        }
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

    /// <summary>
    /// The value a column sorts on, which is what its caret's direction is measured against. The price comes
    /// from the batch read rather than the row, because that is the one the cheapest order sorts by.
    /// </summary>
    private static Func<MountShelfItem, IComparable> KeyOf(int column, IReadOnlyDictionary<uint, int> prices) =>
        column switch
        {
            MountTable.PatchColumn => item => (IComparable)MountExpansionRules.PatchValue(item.Patch)!.Value,
            MountTable.SeatsColumn => item => (IComparable)item.Seats,
            MountTable.OwnedColumn => item => (IComparable)item.Owned,
            MountTable.RarityColumn => item => (IComparable)item.Community!.OwnedPercent!.Value,
            MountTable.PriceColumn => item => (IComparable)prices[item.Id],
            _ => item => (IComparable)item.Name,
        };

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
        string sourceDetail = "",
        double? ownedPercent = null)
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
            lines,
            ownedPercent is null ? null : new MountCommunity(ownedPercent));
    }
}
