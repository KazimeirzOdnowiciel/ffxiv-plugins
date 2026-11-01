using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The community facts beside a mount: that the parser reads them without turning "unrecorded"
/// into a value, that the rarity line says what the data supports, and that "Rarest first" keeps
/// the mounts it knows nothing about at the end instead of the top.
/// </summary>
public sealed class MountCommunityTests
{
    [Fact]
    public void ParsesTheCommunityFields()
    {
        const string json = """
            {
              "mounts": [
                {
                  "id": 412,
                  "patch": "7.45",
                  "itemId": 50438,
                  "owned": 0.4,
                  "tradeable": true,
                  "customMusic": true,
                  "sources": []
                }
              ]
            }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(412, out MountSources mount));
        MountCommunity community = Assert.IsType<MountCommunity>(mount.Community);
        Assert.Equal(0.4, community.OwnedPercent);
        Assert.True(community.Tradeable);
        Assert.True(community.CustomMusic);
    }

    [Fact]
    public void ARecordedFalseStaysFalseAndIsNotUnknown()
    {
        const string json = """
            { "mounts": [ { "id": 3, "owned": 12.5, "tradeable": false, "customMusic": false } ] }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(3, out MountSources mount));
        Assert.Equal(false, mount.Community!.Tradeable);
        Assert.Equal(false, mount.Community.CustomMusic);
    }

    [Fact]
    public void AMountWithoutCommunityFieldsHasNoCommunity()
    {
        // The shape the dataset had before the three fields were added: it must still parse, and it
        // must not arrive as "owned by 0%" / "not tradeable".
        const string json = """
            { "mounts": [ { "id": 5, "patch": "2.0", "itemId": 1 } ] }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(5, out MountSources mount));
        Assert.Null(mount.Community);
    }

    [Fact]
    public void ExplicitNullsAndAQuotedPercentageAreUnknown()
    {
        // ffxivcollect writes "0.3%" as a string in its own JSON; the generator converts it to a
        // number, and a hand-edited file that left the string behind must not become zero.
        const string json = """
            { "mounts": [ { "id": 6, "owned": "2.5%", "tradeable": null, "customMusic": null } ] }
            """;

        MountShelf shelf = MountShelf.Parse(json);

        Assert.True(shelf.TryGet(6, out MountSources mount));
        Assert.Null(mount.Community);
    }

    [Theory]
    [InlineData(0.3, "Owned by 0.3% of collectors")]
    [InlineData(2, "Owned by 2% of collectors")]
    [InlineData(2.4, "Owned by 2.4% of collectors")]
    [InlineData(96.4, "Owned by 96.4% of collectors")]
    [InlineData(0, "Fewer than 0.1% of collectors own this")]
    [InlineData(0.04, "Fewer than 0.1% of collectors own this")]
    [InlineData(100, "Every collector owns this")]
    [InlineData(99.96, "Every collector owns this")]
    public void DescribeSaysWhatTheNumberSupports(double percent, string expected)
    {
        Assert.Equal(expected, MountRarity.Describe(percent));
    }

    [Fact]
    public void DescribeDrawsNothingForUnrecordedRarity()
    {
        Assert.Equal(string.Empty, MountRarity.Describe(null));
        Assert.Equal(string.Empty, MountRarity.Describe(double.NaN));
    }

    [Theory]
    [InlineData(5.2, "5.2% of collectors")]
    [InlineData(0, "under 0.1% of collectors")]
    [InlineData(100, "every collector")]
    public void DescribeCompactDropsTheClauseInsteadOfTheNumber(double percent, string expected)
    {
        Assert.Equal(expected, MountRarity.DescribeCompact(percent));
    }

    [Fact]
    public void DescribeCompactDrawsNothingWhenTheRarityIsUnknown()
    {
        Assert.Equal(string.Empty, MountRarity.DescribeCompact(null));
    }

    /// <summary>
    /// The table's Owners column and the header's insight line both print this reading, so it is pinned at the
    /// four shapes a share has: an ordinary decimal, a rounded zero, the top of the scale, and unrecorded. It had
    /// no test at all while being the only number the table shows for a mount.
    /// </summary>
    [Theory]
    [InlineData(0.3, "0.3%")]
    [InlineData(2, "2%")]
    [InlineData(2.4, "2.4%")]
    [InlineData(96.4, "96.4%")]
    [InlineData(0, "<0.1%")]
    [InlineData(0.04, "<0.1%")]
    [InlineData(100, "100%")]
    [InlineData(99.96, "100%")]
    public void ShareTextPrintsTheTableReading(double percent, string expected)
    {
        Assert.Equal(expected, MountRarity.ShareText(percent));
    }

    [Fact]
    public void ShareTextDrawsNothingForUnrecordedRarity()
    {
        // Empty, not "0%": an unrecorded share is not a share nobody has, and the cell is left blank.
        Assert.Equal(string.Empty, MountRarity.ShareText(null));
        Assert.Equal(string.Empty, MountRarity.ShareText(double.NaN));
    }

    [Fact]
    public void RarestFirstPutsTheSmallestShareAtTheTop()
    {
        var rows = Apply(
            MountOrder.RarestFirst,
            Item("Common", 87.8, 1),
            Item("Rare", 0.3, 2),
            Item("Uncommon", 12.5, 3));

        Assert.Equal(new[] { "Rare", "Uncommon", "Common" }, Names(rows));
    }

    [Fact]
    public void RarestFirstKeepsUnrecordedRarityAtTheEnd()
    {
        // The trap "Newest first" fell into: an unknown value must never sort as the smallest.
        var rows = Apply(
            MountOrder.RarestFirst,
            Item("Unknown", null, 1),
            Item("Known", 40, 2));

        Assert.Equal(new[] { "Known", "Unknown" }, Names(rows));
    }

    [Fact]
    public void RarestFirstFallsBackToTheNameOnATie()
    {
        var rows = Apply(
            MountOrder.RarestFirst,
            Item("Zebra", 5, 1),
            Item("Alpha", 5, 2));

        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(rows));
    }

    [Fact]
    public void CommonestFirstIsRarestFirstReversed()
    {
        // The descending direction is the ascending comparator read backwards, not a second rule: the
        // acceptance for the Owners column's second click is that these two lists are mirrors.
        MountShelfItem[] items =
        [
            Item("Beta", 12.5, 1),
            Item("Alpha", 0.3, 2),
            Item("Delta", 99.9, 3),
            Item("Gamma", 48.7, 4),
        ];

        string[] rarest = Names(Apply(MountOrder.RarestFirst, items));
        string[] commonest = Names(Apply(MountOrder.CommonestFirst, items));

        Assert.Equal(rarest, commonest.Reverse().ToArray());
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma", "Delta" }, rarest);
    }

    [Fact]
    public void CommonestFirstKeepsUnrecordedRarityAtTheEndToo()
    {
        // Negating the whole comparator would have put the unknowns on top of this order - the trap
        // "Newest first" and "Rarest first" each fell into once. Unknown stays last in both directions.
        var rows = Apply(
            MountOrder.CommonestFirst,
            Item("Unknown", null, 1),
            Item("Known", 40, 2));

        Assert.Equal(new[] { "Known", "Unknown" }, Names(rows));
    }

    [Fact]
    public void CommonestFirstStillFallsBackToTheNameOnATie()
    {
        // A reversed tie would make two mounts at one share swap places depending on the arrow clicked.
        var rows = Apply(
            MountOrder.CommonestFirst,
            Item("Zebra", 5, 1),
            Item("Alpha", 5, 2));

        Assert.Equal(new[] { "Alpha", "Zebra" }, Names(rows));
    }

    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.9, true)]
    [InlineData(0.96, false)]
    [InlineData(1, false)]
    [InlineData(12.5, false)]
    public void ARareMountIsOneUnderOnePercentAfterRounding(double percent, bool expected) =>
        // Rounded first, so the badge can never call "1%" rare while the number beside it says 1%.
        Assert.Equal(expected, MountRarity.IsRare(percent));

    [Fact]
    public void AnUnrecordedShareIsNotRare()
    {
        Assert.False(MountRarity.IsRare(null));
        Assert.False(MountRarity.IsRare(double.NaN));
    }

    private static IReadOnlyList<MountShelfItem> Apply(MountOrder order, params MountShelfItem[] items) =>
        new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, order).Apply(items);

    private static string[] Names(IReadOnlyList<MountShelfItem> items)
    {
        var names = new string[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            names[index] = items[index].Name;
        }

        return names;
    }

    private static MountShelfItem Item(string name, double? ownedPercent, uint id) =>
        new(
            id,
            name,
            0u,
            0u,
            1,
            "7.0",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            new MountCommunity(ownedPercent));
}
