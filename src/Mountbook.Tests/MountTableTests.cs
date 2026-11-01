using System;
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
    [InlineData(MountTable.RarityColumn, false, MountOrder.CommonestFirst)]
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
    [InlineData(MountOrder.CommonestFirst, MountTable.RarityColumn)]
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
            MountOrder.OwnedFirst, MountOrder.RarestFirst, MountOrder.Cheapest, MountOrder.CommonestFirst,
        })
        {
            int column = MountTable.ColumnFor(order);

            // The two orders the caret reports as descending are the descending ones: "Newest first" and
            // "Commonest first" are the second click on their column, so they round-trip at false.
            Assert.Equal(order, MountTable.OrderFor(column, !MountTable.Descends(order)));
        }
    }

    [Theory]
    [InlineData(MountTable.NameColumn)]
    [InlineData(MountTable.PatchColumn)]
    [InlineData(MountTable.SeatsColumn)]
    [InlineData(MountTable.SourceColumn)]
    [InlineData(MountTable.OwnedColumn)]
    [InlineData(MountTable.RarityColumn)]
    [InlineData(MountTable.PriceColumn)]
    public void ASortReportThatIsNotAClickChangesNothing(int column)
    {
        // ImGui keeps a sort spec per table id, across sessions, and prefers it over the column the table
        // declares as its default - so a table reports the spec it restored, on the frames after it is
        // created, with no click behind it. Believing one replaced the player's saved order.
        Assert.Null(MountTable.ClickedOrder(column, descending: true, pressed: false));
        Assert.Null(MountTable.ClickedOrder(column, descending: false, pressed: false));
    }

    [Theory]
    [InlineData(MountTable.PatchColumn, true, MountOrder.NewestFirst)]
    [InlineData(MountTable.PatchColumn, false, MountOrder.OldestFirst)]
    [InlineData(MountTable.RarityColumn, true, MountOrder.CommonestFirst)]
    [InlineData(MountTable.RarityColumn, false, MountOrder.RarestFirst)]
    [InlineData(MountTable.OwnedColumn, true, MountOrder.OwnedFirst)]
    [InlineData(MountTable.OwnedColumn, false, MountOrder.OwnedFirst)]
    [InlineData(MountTable.PriceColumn, false, MountOrder.Cheapest)]
    public void AHeaderClickAsksForTheOrderItsCaretShows(int column, bool descending, MountOrder expected)
    {
        // The direction is Glasswork's own - it reports descending - and reading it as ascending inverted
        // the two columns that have two orders: the caret pointed one way and the rows went the other.
        Assert.Equal(expected, MountTable.ClickedOrder(column, descending, pressed: true));
    }

    [Fact]
    public void TheSourceColumnHasNoOrderEvenWhenClicked() =>
        Assert.Null(MountTable.ClickedOrder(MountTable.SourceColumn, descending: false, pressed: true));

    [Theory]
    [InlineData(MountOrder.Name)]
    [InlineData(MountOrder.OldestFirst)]
    [InlineData(MountOrder.NewestFirst)]
    [InlineData(MountOrder.Seats)]
    [InlineData(MountOrder.OwnedFirst)]
    [InlineData(MountOrder.RarestFirst)]
    [InlineData(MountOrder.Cheapest)]
    [InlineData(MountOrder.CommonestFirst)]
    public void EveryOrderIsDescribedByItsOwnColumnAndDirection(MountOrder order)
    {
        int column = MountTable.ColumnFor(order);
        bool descending = MountTable.Descends(order);

        Assert.True(MountTable.DescribedBy(order, column, descending));

        // The other direction on the same column is what the caret repair exists for: it is what the table
        // shows for a descending order the default sort declared ascending, and for a spec it restored.
        Assert.False(MountTable.DescribedBy(order, column, !descending));

        // And a report name a column at all.
        Assert.False(MountTable.DescribedBy(order, MountTable.SourceColumn, descending));
    }

    [Fact]
    public void OnlyTheOrdersThatAreASecondClickDescend()
    {
        // Pinned by count as well as by name: a new order that sorts descending has to be named in
        // Descends, or its caret claims ascending over rows sorted the other way - which is the bug this
        // whole pair of rules was written for.
        Assert.True(MountTable.Descends(MountOrder.NewestFirst));
        Assert.True(MountTable.Descends(MountOrder.CommonestFirst));

        var descending = 0;
        foreach (MountOrder order in Enum.GetValues<MountOrder>())
        {
            if (MountTable.Descends(order))
            {
                descending++;
            }
        }

        Assert.Equal(2, descending);
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
