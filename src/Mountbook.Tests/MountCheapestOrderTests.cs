using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// "Cheapest" orders by the batch price the market read gave each mount. The one rule that matters is
/// that a mount with no known price is parked at the end: an unknown price is not a cheap price, and
/// treating the missing value as 0 would put every unpriced mount at the top of the list. "Newest first"
/// and "Rarest first" each needed a branch for the same reason.
/// </summary>
public sealed class MountCheapestOrderTests
{
    [Fact]
    public void CheapestPutsTheLowestPriceFirst()
    {
        var items = new[] { Item(1, "Alpha"), Item(2, "Beta"), Item(3, "Gamma") };
        var prices = new Dictionary<uint, int> { [1u] = 900000, [2u] = 1500, [3u] = 2401000 };

        var rows = Apply(items, prices);

        Assert.Equal(new[] { "Beta", "Alpha", "Gamma" }, Names(rows));
    }

    [Fact]
    public void MountsWithNoKnownPriceSortLast()
    {
        var items = new[] { Item(1, "Alpha"), Item(2, "Beta"), Item(3, "Gamma") };
        var prices = new Dictionary<uint, int> { [2u] = 5000 };

        var rows = Apply(items, prices);

        // The two unpriced mounts keep name order between themselves; they do not outrank the priced one.
        Assert.Equal(new[] { "Beta", "Alpha", "Gamma" }, Names(rows));
    }

    [Fact]
    public void APriceOfZeroIsNobodySellingAndSortsLast()
    {
        // 0 is what a batch answer leaves for "asked, nothing for sale": a real answer, but not a price.
        var items = new[] { Item(1, "Alpha"), Item(2, "Beta") };
        var prices = new Dictionary<uint, int> { [1u] = 0, [2u] = 700000 };

        var rows = Apply(items, prices);

        Assert.Equal(new[] { "Beta", "Alpha" }, Names(rows));
    }

    [Fact]
    public void EqualPricesFallBackToTheName()
    {
        var items = new[] { Item(1, "Zebra"), Item(2, "Alpha") };
        var prices = new Dictionary<uint, int> { [1u] = 1000, [2u] = 1000 };

        var rows = Apply(items, prices);

        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(rows));
    }

    [Fact]
    public void WithoutAnyPricesTheOrderStillWorks()
    {
        // The list is drawn before the batch lands, and the window passes no map at all when nothing is
        // known: that must be a name order, not an exception on the draw path.
        var items = new[] { Item(1, "Zebra"), Item(2, "Alpha") };

        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(Apply(items, null)));

        var query = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Cheapest);
        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(query.Apply(items, null, null)));
    }

    private static IReadOnlyList<MountShelfItem> Apply(
        MountShelfItem[] items,
        IReadOnlyDictionary<uint, int>? prices) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Cheapest)
            .Apply(items, null, prices);

    private static string[] Names(IReadOnlyList<MountShelfItem> items)
    {
        var names = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            names[index] = items[index].Name;
        }

        return names;
    }

    private static MountShelfItem Item(uint id, string name) =>
        new(id, name, 0u, 0u, 1, "7.0", false, string.Empty, string.Empty, string.Empty, []);
}
