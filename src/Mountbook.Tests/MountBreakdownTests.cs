using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The per-expansion breakdown: grouping by patch major, release order, and the fallback bucket.
/// </summary>
public sealed class MountBreakdownTests
{
    [Fact]
    public void GroupsByPatchMajorInReleaseOrder()
    {
        var rows = MountBreakdown.ByExpansion(
        [
            Item("A", "6.1", true),
            Item("B", "2.5", false),
            Item("C", "6.4", false),
            Item("D", "4.0", true),
        ]);

        Assert.Equal(3, rows.Count);
        Assert.Equal("A Realm Reborn", rows[0].Name);
        Assert.Equal((0, 1), (rows[0].Owned, rows[0].Total));
        Assert.Equal("Stormblood", rows[1].Name);
        Assert.Equal((1, 1), (rows[1].Owned, rows[1].Total));
        Assert.Equal("Endwalker", rows[2].Name);
        Assert.Equal((1, 2), (rows[2].Owned, rows[2].Total));
        Assert.Equal(50, rows[2].Percent);
    }

    [Fact]
    public void SkipsExpansionsWithNoMounts()
    {
        var rows = MountBreakdown.ByExpansion([Item("A", "7.0", false)]);

        Assert.Single(rows);
        Assert.Equal("Dawntrail", rows[0].Name);
        Assert.Equal("DT", rows[0].ShortName);
    }

    /// <summary>
    /// The search and the expansion filter must agree on every patch string: they used to parse it with
    /// two different functions (one trimming, one not), so <c>" 6.1"</c> filtered as Unknown while a
    /// search for "endwalker" still found the same mount.
    /// </summary>
    [Theory]
    [InlineData("6.1")]
    [InlineData(" 6.1")]
    [InlineData("6 .1")]
    [InlineData("2.0")]
    [InlineData("3.55a")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("9.9")]
    public void TheSearchAndTheFilterReadAPatchTheSameWay(string patch)
    {
        MountShelfItem item = Item("A", patch, true);

        ExpansionProgress? mapped = MountBreakdown.ExpansionForPatch(patch);

        bool filterSaysUnknown =
            new MountQuery(null, MountOwnership.All, MountExpansion.Unknown, MountOrder.Name).Apply([item]).Count == 1;

        Assert.Equal(mapped is null, filterSaysUnknown);

        if (mapped is not null)
        {
            // Whatever the search matches by name must be the same expansion the filter selects.
            Assert.NotEmpty(
                new MountQuery(mapped.Name, MountOwnership.All, MountExpansion.All, MountOrder.Name).Apply([item]));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("1.0")]
    [InlineData("9.9")]
    public void UnusablePatchesFallIntoOther(string patch)
    {
        var rows = MountBreakdown.ByExpansion([Item("A", patch, true)]);

        Assert.Single(rows);
        Assert.Equal("Other", rows[0].Name);
        Assert.Equal((1, 1), (rows[0].Owned, rows[0].Total));
    }

    [Fact]
    public void EmptyInputYieldsNoRows() => Assert.Empty(MountBreakdown.ByExpansion([]));

    [Fact]
    public void TotalsAlwaysAddUp()
    {
        var rows = MountBreakdown.ByExpansion(
        [
            Item("A", "2.1", true),
            Item("B", "3.3", true),
            Item("C", "", false),
            Item("D", "6.5", false),
        ]);

        var total = 0;

        foreach (var row in rows)
        {
            total += row.Total;
        }

        Assert.Equal(4, total);
    }

    private static MountShelfItem Item(string name, string patch, bool owned) =>
        new(1u, name, 0u, 0u, 1, patch, owned, string.Empty, string.Empty, string.Empty, []);
}
