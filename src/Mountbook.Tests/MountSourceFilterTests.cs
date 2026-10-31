using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The acquire-method filter: "what do I still need from PvP" without a search that also matches lore.
/// </summary>
public sealed class MountSourceFilterTests
{
    private static MountQuery Query(string? source) =>
        new(null, MountOwnership.All, MountExpansion.All, MountOrder.Name) { Source = source };

    private static MountShelfItem Item(uint id, string name, bool owned, params string[] categories)
    {
        var sources = new List<MountSourceLine>(categories.Length);

        foreach (string category in categories)
        {
            sources.Add(new MountSourceLine(category, "detail", string.Empty, 0));
        }

        return new MountShelfItem(
            id,
            name,
            0,
            0,
            1,
            "6.1",
            owned,
            string.Empty,
            string.Empty,
            string.Empty,
            sources);
    }

    private static IReadOnlyList<MountShelfItem> Shelf() =>
    [
        Item(1, "Trial Mount", true, "Trial"),
        Item(2, "PvP Mount", true, "PvP"),
        Item(3, "Both Mount", false, "PvP", "Achievement"),
        Item(4, "Event Mount", false, "Event"),
    ];

    [Fact]
    public void KeepsOnlyMountsWithThatCategory()
    {
        IReadOnlyList<MountShelfItem> result = Query("PvP").Apply(Shelf());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == 2);
        Assert.Contains(result, item => item.Id == 3);
    }

    [Fact]
    public void MatchesCaseInsensitively()
    {
        Assert.Equal(2, Query("pvp").Apply(Shelf()).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyFilterKeepsEverything(string? source)
    {
        Assert.Equal(4, Query(source).Apply(Shelf()).Count);
    }

    [Fact]
    public void AnUnknownCategoryMatchesNothing()
    {
        Assert.Empty(Query("Raid").Apply(Shelf()));
    }

    [Fact]
    public void OwnershipCountsRespectTheSourceFilter()
    {
        IReadOnlyList<MountShelfItem> shelf = Shelf();

        int[] counts = Query("PvP").OwnershipCounts(shelf, new HashSet<uint>());

        Assert.Equal(2, counts[0]);
        Assert.Equal(1, counts[1]);
        Assert.Equal(1, counts[2]);
        Assert.Equal(0, counts[3]);
    }

    [Fact]
    public void SourceTypesAreDistinctAndSorted()
    {
        string[] types = MountQuery.SourceTypes(Shelf());

        Assert.Equal(["Achievement", "Event", "PvP", "Trial"], types);
    }
}
