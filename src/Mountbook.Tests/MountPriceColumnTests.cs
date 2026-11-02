using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The Price column's two rules: when the window reads prices at all, and which rows have one worth
/// drawing.
/// </summary>
/// <remarks>
/// The first rule is what the column of blanks was. A read was offered only while the list was ordered
/// cheapest-first, so the table's Price column - which is in the table whatever the order - showed
/// whatever that one visit had left in the cache: one price, a gap, another price. The rule is now "the
/// price is on screen or is the sort key", and a table frame is the request.
/// </remarks>
public sealed class MountPriceColumnTests
{
    [Theory]
    [InlineData(MountOrder.Name)]
    [InlineData(MountOrder.OldestFirst)]
    [InlineData(MountOrder.NewestFirst)]
    [InlineData(MountOrder.Seats)]
    [InlineData(MountOrder.OwnedFirst)]
    [InlineData(MountOrder.RarestFirst)]
    [InlineData(MountOrder.CommonestFirst)]
    [InlineData(MountOrder.Cheapest)]
    [InlineData(MountOrder.Random)]
    public void TheTableDrawsItsPriceColumnInEveryOrder(MountOrder order) =>
        // The column is the table's, not the order's: owned-first, name or rarity, it is on screen and it
        // has to be filled. This is the case the client showed as 22 blank cells of 23.
        Assert.True(MountPriceColumn.Wanted(MountViewMode.Table, order));

    [Fact]
    public void TheCheapestOrderAsksInTheCardGridToo() =>
        // No column shows a price there, but the sort key is one: without a read the grid would order by
        // a price it never asked for and park every mount at the end as "unknown".
        Assert.True(MountPriceColumn.Wanted(MountViewMode.Grid, MountOrder.Cheapest));

    [Theory]
    [InlineData(MountOrder.Name)]
    [InlineData(MountOrder.OldestFirst)]
    [InlineData(MountOrder.NewestFirst)]
    [InlineData(MountOrder.Seats)]
    [InlineData(MountOrder.OwnedFirst)]
    [InlineData(MountOrder.RarestFirst)]
    [InlineData(MountOrder.CommonestFirst)]
    [InlineData(MountOrder.Random)]
    public void NothingElseAsksForAPriceItCannotShow(MountOrder order) =>
        // The window is not a price reader: browsing cards by name must not spend a batch on a number
        // nothing on screen prints.
        Assert.False(MountPriceColumn.Wanted(MountViewMode.Grid, order));

    [Fact]
    public void APricedRowDrawsItsPrice() =>
        Assert.Equal(2401000, MountPriceColumn.PriceOf(Prices((7u, 2401000)), 7u));

    [Fact]
    public void AnUnknownRowDrawsNothing() =>
        // Never asked at all: no cell rather than a 0, which would claim the mount is free.
        Assert.Equal(0, MountPriceColumn.PriceOf(Prices((7u, 2401000)), 8u));

    [Fact]
    public void APriceOfZeroIsNobodySellingAndDrawsNothing() =>
        // 0 is what a batch answer leaves for "asked, nothing for sale": a real answer, but not a price.
        Assert.Equal(0, MountPriceColumn.PriceOf(Prices((7u, 0)), 7u));

    [Fact]
    public void TheColumnReservesRoomForAWiderPriceThanAnyMountIsListedAt() =>
        // The column is a fixed width measured from this label, so the reserve has to be at least as wide
        // as the widest number a row can print - and it is measured through the same formatter the cells
        // use, so the two cannot disagree about the grouping.
        Assert.Equal("999,999,999", MountTable.PriceText(MountTable.WidestPrice));

    private static Dictionary<uint, int> Prices(params (uint MountId, int Price)[] rows)
    {
        var prices = new Dictionary<uint, int>();

        foreach ((uint mountId, int price) in rows)
        {
            prices[mountId] = price;
        }

        return prices;
    }
}
