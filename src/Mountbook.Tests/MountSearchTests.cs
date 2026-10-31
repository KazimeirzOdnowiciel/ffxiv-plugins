using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// What the search box matches: the mount itself, its patch and expansion, and the source lines a
/// player would type ("trial", "endwalker", "6.1").
/// </summary>
public sealed class MountSearchTests
{
    [Theory]
    [InlineData("kamuy")]
    [InlineData("KAMUY")]
    public void MatchesTheNameCaseInsensitively(string search) => Assert.Single(Apply(search, Item("Auspicious Kamuy")));

    [Fact]
    public void MatchesDescriptionAndEnhancedDescription()
    {
        Assert.Single(Apply("guard the gates", Item("Auspicious Kamuy", description: "said to guard the gates")));
        Assert.Single(Apply("plucked string", Item("Auspicious Kamuy", enhanced: "hums like a plucked string")));
    }

    [Fact]
    public void MatchesTheTooltip()
    {
        Assert.Single(Apply("from the store", Item("Auspicious Kamuy", tooltip: "Obtained from the store")));
    }

    [Fact]
    public void MatchesThePatch()
    {
        Assert.Single(Apply("6.1", Item("Auspicious Kamuy", patch: "6.1")));
    }

    [Theory]
    [InlineData("endwalker")]
    [InlineData("EW")]
    public void MatchesTheExpansionName(string search) =>
        Assert.Single(Apply(search, Item("Auspicious Kamuy", patch: "6.1")));

    [Fact]
    public void MatchesTheSourceCategory()
    {
        var item = Item(
            "Auspicious Kamuy",
            sources: [new MountSourceLine("Trial", "The Shrine of Faith", "instanceContent", 1u)]);

        Assert.Single(Apply("trial", item));
        Assert.Single(Apply("shrine", item));
    }

    [Fact]
    public void UnknownExpansionDoesNotMatchEverything()
    {
        Assert.Empty(Apply("endwalker", Item("Plain Mount", patch: string.Empty)));
    }

    // --- operators -------------------------------------------------------
    // A term written key:value narrows by a fact; a fact the dataset does not record never satisfies a
    // constraint, so an unrecorded "tradeable" is neither yes nor no.

    [Fact]
    public void TradeableSelectsOnlyTradeableMounts()
    {
        Assert.Equal(new[] { "Lamp" }, Names("tradeable:y"));
        Assert.Equal(new[] { "Barge", "Chocobo" }, Names("tradeable:n"));
    }

    [Fact]
    public void AnUnrecordedFlagSatisfiesNeitherYesNorNo()
    {
        // Rock has no community data at all.
        Assert.DoesNotContain("Rock", Names("tradeable:n"));
        Assert.DoesNotContain("Rock", Names("music:n"));
    }

    [Fact]
    public void BardingSelectsByTheSheetFlag()
    {
        // Unlike the community flags this one is never unrecorded: the sheet always says whether a
        // gear slot exists, so "no" is a real answer for every mount that does not take barding.
        Assert.Equal(new[] { "Chocobo" }, Names("barding:y"));
        Assert.Equal(new[] { "Barge", "Lamp", "Rock" }, Names("barding:n"));
    }

    [Fact]
    public void FlyingSelectsByTheSheetFlag()
    {
        // A sheet flag, so unlike tradeable: there is no "unrecorded" state to preserve: a mount that
        // cannot fly really is a no, and the operator can say so.
        Assert.Equal(new[] { "Barge", "Chocobo" }, Names("flying:y"));
        Assert.Equal(new[] { "Lamp", "Rock" }, Names("flying:n"));
        Assert.Equal(new[] { "Barge", "Chocobo" }, Names("fly:y"));
    }

    [Fact]
    public void MusicSelectsByTheOwnMusicFlag()
    {
        Assert.Equal(new[] { "Lamp" }, Names("music:y"));
        Assert.Equal(new[] { "Chocobo" }, Names("music:n"));
    }

    [Theory]
    [InlineData("seats:2", new[] { "Barge" })]
    [InlineData("seats:>=2", new[] { "Barge" })]
    [InlineData("seats:<2", new[] { "Chocobo", "Lamp", "Rock" })]
    public void SeatsCompares(string search, string[] expected) => Assert.Equal(expected, Names(search));

    [Theory]
    [InlineData("patch:7.45", new[] { "Lamp" })]
    [InlineData("patch:>=7", new[] { "Lamp", "Rock" })]
    [InlineData("patch:<7", new[] { "Barge", "Chocobo" })]
    public void PatchCompares(string search, string[] expected) => Assert.Equal(expected, Names(search));

    [Theory]
    [InlineData("owned:<1", new[] { "Lamp" })]
    [InlineData("owned:>=50", new[] { "Chocobo" })]
    [InlineData("owned:<1%", new[] { "Lamp" })]
    public void OwnedComparesTheShareOfCollectors(string search, string[] expected) =>
        Assert.Equal(expected, Names(search));

    [Fact]
    public void OperatorsCombineWithFreeText()
    {
        Assert.Equal(new[] { "Lamp" }, Names("lamp tradeable:y"));
        Assert.Empty(Names("chocobo tradeable:y"));
    }

    [Fact]
    public void AnUnknownKeyStaysFreeText()
    {
        var item = Item(
            "Auspicious Kamuy",
            sources: [new MountSourceLine("Trial", "Trial: Byakko Unchained", string.Empty, 0u)]);

        // The colon in a source line must not turn the search into an operator the player never typed.
        Assert.Single(Apply("Trial: Byakko", item));
        Assert.Empty(Names("nonsense:y"));
    }

    [Fact]
    public void AnUnparseableValueFallsBackToFreeText()
    {
        // "owned:lots" is not a constraint, so it is searched for as text like any other word - which is
        // honest: the empty state then says no mount matches, instead of quietly ignoring the typo and
        // showing a list the player did not ask for.
        Assert.Empty(Names("owned:lots"));
        Assert.Empty(Names("owned:lots chocobo"));
    }

    private static string[] Names(string search) => [.. Apply(search, Shelf()).Select(item => item.Name)];

    /// <summary>
    /// Four mounts covering both flags, the seat count, the patch and an unrecorded community. Names sort
    /// alphabetically, which is the default order, so the expectations read in list order.
    /// </summary>
    private static IReadOnlyList<MountShelfItem> Shelf() =>
    [
        Item("Chocobo", patch: "2.0", id: 1u, community: new MountCommunity(96.4, false, false), barding: true, flies: true),
        Item("Lamp", patch: "7.45", id: 2u, community: new MountCommunity(0.4, true, true)),
        Item("Barge", patch: "6.5", id: 3u, seats: 2, community: new MountCommunity(22.3, false, null), flies: true),
        Item("Rock", patch: "7.0", id: 4u),
    ];

    private static IReadOnlyList<MountShelfItem> Apply(string search, IReadOnlyList<MountShelfItem> items) =>
        new MountQuery(search, MountOwnership.All, MountExpansion.All, MountOrder.Name).Apply(items);

    private static IReadOnlyList<MountShelfItem> Apply(string search, MountShelfItem item) => Apply(search, [item]);

    private static MountShelfItem Item(
        string name,
        string patch = "6.0",
        string description = "",
        string enhanced = "",
        string tooltip = "",
        IReadOnlyList<MountSourceLine>? sources = null,
        uint id = 1u,
        int seats = 1,
        MountCommunity? community = null,
        bool barding = false,
        bool flies = false) =>
        new(id, name, 0u, 0u, seats, patch, false, description, enhanced, tooltip, sources ?? [], community, barding, flies);
}
