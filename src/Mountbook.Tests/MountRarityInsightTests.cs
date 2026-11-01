using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The header's one insight: which mount is the rarest the player is still missing, and the sentence that
/// names it. The failure modes are the ones a collector would notice - naming a mount they already have,
/// naming one whose share nobody recorded, or naming a different mount on the next frame.
/// </summary>
public sealed class MountRarityInsightTests
{
    [Fact]
    public void TheRarestUnownedMountWins()
    {
        RarestMissing? found = MountRarityInsight.Find(
        [
            Item("Common", 87.8, true, 1),
            Item("MissingRare", 0.3, false, 2),
            Item("OwnedRarest", 0.1, true, 3),
        ]);

        Assert.Equal(new RarestMissing(2, "MissingRare", 0.3), found);
    }

    [Fact]
    public void EverythingOwnedNamesNothing()
    {
        // The acceptance for the header line: with the collection finished there is no sentence to draw,
        // not a sentence about the mount the player "almost" has.
        Assert.Null(MountRarityInsight.Find(
        [
            Item("One", 0.4, true, 1),
            Item("Two", 12.5, true, 2),
        ]));
    }

    [Fact]
    public void AnUnrecordedShareIsNeverTheRarest()
    {
        // "Unknown" is not "rare": the same rule the ordering applies. A mount with no recorded share can
        // neither win the line nor be described by it.
        RarestMissing? found = MountRarityInsight.Find(
        [
            Item("Unknown", null, false, 1),
            Item("Recorded", 40, false, 2),
        ]);

        Assert.Equal(new RarestMissing(2, "Recorded", 40), found);
    }

    [Fact]
    public void NothingToDescribeWithoutARecordedShare()
    {
        Assert.Null(MountRarityInsight.Find([Item("Unknown", null, false, 1)]));
    }

    [Fact]
    public void AnEmptyCollectionNamesNothing() => Assert.Null(MountRarityInsight.Find([]));

    [Fact]
    public void ATieIsSettledByName()
    {
        // Two mounts at the same share must not swap places between frames, whichever order the list is in.
        RarestMissing? first = MountRarityInsight.Find(
        [
            Item("Zebra", 0.4, false, 1),
            Item("Alpha", 0.4, false, 2),
        ]);

        RarestMissing? second = MountRarityInsight.Find(
        [
            Item("Alpha", 0.4, false, 2),
            Item("Zebra", 0.4, false, 1),
        ]);

        Assert.Equal(2u, first!.Value.Id);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheLineNamesTheMountAndItsShare()
    {
        RarestMissing? found = MountRarityInsight.Find([Item("Astrope", 0.5, false, 160)]);

        Assert.Equal("Rarest missing: Astrope (0.5%)", MountRarityInsight.Line(found));
        Assert.Equal("Rarest: Astrope (0.5%)", MountRarityInsight.CompactLine(found));
    }

    [Fact]
    public void ARoundedZeroShareIsFewerThanOneStep()
    {
        // The site rounds to one decimal: 0.04 is "<0.1%", never "0%", which would claim nobody owns it.
        RarestMissing? found = MountRarityInsight.Find([Item("Ghost", 0.04, false, 7)]);

        Assert.Equal("Rarest missing: Ghost (<0.1%)", MountRarityInsight.Line(found));
    }

    [Fact]
    public void NothingIsDrawnWithoutAMount()
    {
        Assert.Equal(string.Empty, MountRarityInsight.Line(null));
        Assert.Equal(string.Empty, MountRarityInsight.CompactLine(null));
    }

    private static MountShelfItem Item(string name, double? ownedPercent, bool owned, uint id) =>
        new(
            id,
            name,
            0u,
            0u,
            1,
            "7.0",
            owned,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            new MountCommunity(ownedPercent));
}
