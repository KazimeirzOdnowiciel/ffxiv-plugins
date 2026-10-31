using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The dense table's cells and its header sort: every unrecorded fact is an empty cell rather than a
/// placeholder, and a header click has to mean one of the orders the list already understands.
/// </summary>
public sealed class MountTableTests
{
    [Fact]
    public void PatchIsBlankWhenTheDatasetDoesNotRecordOne() =>
        Assert.Equal(string.Empty, MountTable.PatchText(Item(patch: string.Empty)));

    [Fact]
    public void PatchIsTheValueTheDatasetCarries() =>
        Assert.Equal("6.1", MountTable.PatchText(Item(patch: "6.1")));

    [Fact]
    public void SeatsIsABareNumber()
    {
        Assert.Equal("1", MountTable.SeatsText(Item(seats: 1)));
        Assert.Equal("4", MountTable.SeatsText(Item(seats: 4)));
    }

    [Fact]
    public void SourceIsTheFirstObtainableCategory()
    {
        MountShelfItem item = Item(
            sources: [new MountSourceLine("Trial", "The Pool of Tribute", string.Empty, 0), new MountSourceLine("Achievement", "Goodly Sea-Snake", string.Empty, 0)]);

        Assert.Equal("Trial", MountTable.SourceText(item));
    }

    [Fact]
    public void SourceIsBlankWithoutObtainData() => Assert.Equal(string.Empty, MountTable.SourceText(Item()));

    [Fact]
    public void CollectedIsYesOrNothing()
    {
        Assert.Equal("Yes", MountTable.OwnedText(Item(owned: true)));
        Assert.Equal(string.Empty, MountTable.OwnedText(Item(owned: false)));
    }

    [Fact]
    public void RarityIsAPercentage()
    {
        Assert.Equal("3.4%", MountTable.RarityText(Item(ownedPercent: 3.4)));
        Assert.Equal("100%", MountTable.RarityText(Item(ownedPercent: 99.97)));
    }

    [Fact]
    public void RoundedZeroRaritySaysFewerThanTheStep()
    {
        // The site rounds to one decimal, so 0.04 means "fewer than 0.1%", not "nobody".
        Assert.Equal("<0.1%", MountTable.RarityText(Item(ownedPercent: 0.04)));
        Assert.Equal("<0.1%", MountTable.RarityText(Item(ownedPercent: 0)));
    }

    [Fact]
    public void UnrecordedRarityIsBlank()
    {
        Assert.Equal(string.Empty, MountTable.RarityText(Item()));
        Assert.Equal(string.Empty, MountTable.RarityText(Item(community: new MountCommunity(default, true, false))));
    }

    [Theory]
    [InlineData(MountTable.NameColumn, true, MountOrder.Name)]
    [InlineData(MountTable.NameColumn, false, MountOrder.Name)]
    [InlineData(MountTable.PatchColumn, true, MountOrder.OldestFirst)]
    [InlineData(MountTable.PatchColumn, false, MountOrder.NewestFirst)]
    [InlineData(MountTable.SeatsColumn, true, MountOrder.Seats)]
    [InlineData(MountTable.OwnedColumn, true, MountOrder.OwnedFirst)]
    [InlineData(MountTable.RarityColumn, true, MountOrder.RarestFirst)]
    public void HeaderClicksMapOntoTheOrdersTheListAlreadyHas(int column, bool ascending, MountOrder expected) =>
        Assert.Equal(expected, MountTable.OrderFor(column, ascending));

    [Fact]
    public void TheSourceColumnHasNoOrderOfItsOwn() =>
        Assert.Null(MountTable.OrderFor(MountTable.SourceColumn, true));

    [Theory]
    [InlineData(MountOrder.Name, MountTable.NameColumn)]
    [InlineData(MountOrder.OldestFirst, MountTable.PatchColumn)]
    [InlineData(MountOrder.NewestFirst, MountTable.PatchColumn)]
    [InlineData(MountOrder.Seats, MountTable.SeatsColumn)]
    [InlineData(MountOrder.OwnedFirst, MountTable.OwnedColumn)]
    [InlineData(MountOrder.RarestFirst, MountTable.RarityColumn)]
    public void EveryOrderHasAColumnToShowItIn(MountOrder order, int expected) =>
        Assert.Equal(expected, MountTable.ColumnFor(order));

    [Fact]
    public void DefaultSortIsTheColumnOfTheCurrentOrder()
    {
        // Round trip: the column the table sorts by first must map back to the order it started in,
        // or opening the table would silently reorder the list.
        foreach (MountOrder order in new[]
        {
            MountOrder.Name, MountOrder.OldestFirst, MountOrder.NewestFirst, MountOrder.Seats,
            MountOrder.OwnedFirst, MountOrder.RarestFirst, MountOrder.Cheapest,
        })
        {
            int column = MountTable.ColumnFor(order);

            Assert.Equal(order, MountTable.OrderFor(column, order != MountOrder.NewestFirst));
        }
    }

    [Fact]
    public void PriceIsBlankWhenItIsUnknown() =>
        // Never asked, nobody sells it and untradeable all read the same way: no number, because a 0
        // in this column would claim the mount is free.
        Assert.Equal(string.Empty, MountTable.PriceText(0));

    [Fact]
    public void PriceIsWrittenAsABareGroupedNumber() =>
        // The header says "Price", so the cell does not repeat "gil" on every row.
        Assert.Equal("2,401,000", MountTable.PriceText(2401000));

    [Fact]
    public void PriceColumnIsTheCheapestOrder()
    {
        Assert.Equal(MountTable.PriceColumn, MountTable.ColumnFor(MountOrder.Cheapest));
        Assert.Equal(MountOrder.Cheapest, MountTable.OrderFor(MountTable.PriceColumn, true));
    }

    private static MountShelfItem Item(
        string patch = "",
        int seats = 1,
        bool owned = false,
        double? ownedPercent = null,
        MountCommunity? community = null,
        IReadOnlyList<MountSourceLine>? sources = null) =>
        new(
            1u,
            "Test Mount",
            1u,
            64001u,
            seats,
            patch,
            owned,
            string.Empty,
            string.Empty,
            string.Empty,
            sources ?? [],
            community ?? (ownedPercent is null ? null : new MountCommunity(ownedPercent, null, null)));
}
