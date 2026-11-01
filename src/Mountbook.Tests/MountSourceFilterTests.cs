using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The obtain-type filter: "what do I still need from PvP" without a search that also matches lore.
/// </summary>
/// <remarks>
/// The filter is keyed by <see cref="MountSourceKind"/>, so these tests hold the type and never the label: what
/// is asserted is that two spellings of one type are one filter, that a type's <em>text</em> is not its
/// identity, and that the counts the combo shows are mounts rather than source lines.
/// </remarks>
public sealed class MountSourceFilterTests
{
    private static MountQuery Query(MountSourceKind source) =>
        new(null, MountOwnership.All, MountExpansion.All, MountOrder.Name) { Source = source };

    private static MountQuery Query(string? source) => Query(MountSourceKinds.Parse(source));

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
    public void KeepsOnlyMountsCarryingThatType()
    {
        IReadOnlyList<MountShelfItem> result = Query("PvP").Apply(Shelf());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == 2);
        Assert.Contains(result, item => item.Id == 3);
    }

    [Theory]
    [InlineData("PvP")]
    [InlineData("pvp")]
    [InlineData("PVP")]
    [InlineData("  PvP ")]
    public void AFiltersSpellingFoldsToOneType(string spelling)
    {
        Assert.Equal(2, Query(spelling).Apply(Shelf()).Count);
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
    public void AnyKeepsEverythingAndIsTheStoredNoFilterValue()
    {
        Assert.Equal(4, Query(MountSourceKinds.Any).Apply(Shelf()).Count);
        Assert.True(MountSourceKinds.Parse(string.Empty).IsAny);
        Assert.True(MountSourceKinds.Any.IsAny);
    }

    [Fact]
    public void ATypeTheShelfDoesNotCarryKeepsNothing()
    {
        // Raid is a real type of the taxonomy that this fixture's four mounts do not carry.
        Assert.Empty(Query("Raid").Apply(Shelf()));
        Assert.Empty(Query(MountSourceKinds.Taxonomy[0]).Apply(Shelf()));
    }

    [Fact]
    public void TheFilterSelectsTheTypeRatherThanItsText()
    {
        // The label the combo draws for Trial is "Trial (1)". A row that literally spells that is a different,
        // unknown type: if the filter matched text, this would be a Trial mount.
        IReadOnlyList<MountShelfItem> shelf = [Item(9, "Odd Mount", true, "Trial (1)")];

        Assert.Empty(Query("Trial").Apply(shelf));
        Assert.Single(Query(MountSourceKinds.Parse("Trial (1)")).Apply(shelf));
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
    public void TheTallyCountsMountsRatherThanSourceLines()
    {
        // Two Trial lines on one mount are one Trial mount; a mount with no obtain data carries no type.
        IReadOnlyList<MountShelfItem> shelf =
        [
            Item(1, "Two Trials", true, "Trial", "Trial"),
            Item(2, "One Trial", true, "Trial"),
            Item(3, "Silent", false),
        ];

        IReadOnlyList<MountSourceCount> tally = MountSourceKinds.Tally(shelf);

        MountSourceCount trial = Assert.Single(tally);
        Assert.Equal(MountSourceKinds.Parse("Trial"), trial.Kind);
        Assert.Equal(2, trial.Mounts);
    }

    [Fact]
    public void TheTallyListsOnlyWhatTheShelfCarriesInTaxonomyOrder()
    {
        IReadOnlyList<MountShelfItem> shelf =
        [
            Item(1, "Event", true, "Event"),
            Item(2, "Premium", true, "Premium"),
            Item(3, "Achievement", true, "Achievement"),
        ];

        IReadOnlyList<MountSourceCount> tally = MountSourceKinds.Tally(shelf);

        Assert.Equal(
            ["Premium", "Achievement", "Event"],
            [.. tally.Select(count => count.Kind.Name)]);
        Assert.Equal([0, 1, 6], [.. tally.Select(count => count.Kind.Rank)]);
    }

    [Fact]
    public void AMountCarryingTwoTypesIsCountedUnderBoth()
    {
        IReadOnlyList<MountShelfItem> shelf = [Item(1, "Both", true, "Trial", "Achievement")];

        IReadOnlyList<MountSourceCount> tally = MountSourceKinds.Tally(shelf);

        Assert.Equal(2, tally.Count);
        Assert.All(tally, count => Assert.Equal(1, count.Mounts));
    }
}
