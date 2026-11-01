using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The availability filter: the collector's question ("what can I go and get") as a list filter and as a search
/// term, both reading the class the snapshot states rather than re-deriving it from the obtain types.
/// </summary>
public sealed class MountAvailabilityFilterTests
{
    [Fact]
    public void NoFilterKeepsEveryClass()
    {
        IReadOnlyList<MountShelfItem> items = Sample();

        Assert.Equal(items.Count, Query(null).Apply(items).Count);
    }

    [Fact]
    public void AClassKeepsOnlyThatClass()
    {
        IReadOnlyList<MountShelfItem> kept = Query(MountAvailability.Premium).Apply(Sample());

        Assert.Single(kept);
        Assert.Equal(3u, kept[0].Id);
    }

    [Fact]
    public void AMountWithNoClassIsNotAvailable()
    {
        // "Nothing is known" is not "you can farm it": the unknown row is excluded by the Available filter, which is
        // the whole reason Unknown is a class of its own.
        IReadOnlyList<MountShelfItem> kept = Query(MountAvailability.Available).Apply(Sample());

        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, item => item.Id == 4);
    }

    [Fact]
    public void TheChipCountsRespectTheClassFilterToo()
    {
        // The ownership chips are a second run of the same query; if the class filter were left out of them the pane
        // would say "no mounts match" while a chip claimed some.
        int[] counts = Query(MountAvailability.Available).OwnershipCounts(Sample());

        // Two available mounts: one owned, one not, so the ownership chips agree with the two rows the list holds.
        Assert.Equal(2, counts[0]);
        Assert.Equal(1, counts[1]);
        Assert.Equal(1, counts[2]);
    }

    [Fact]
    public void TheTermSelectsOneClassAndTheSeededViewSelectsTheFarmableOnes()
    {
        // The shipped "Farmable now" view is exactly this: the term over the mounts the player does not own, which
        // is why the term is the preset's whole definition.
        SavedView view = SavedViewRules.FarmableView();
        MountQuery query = new(view.Query, view.Ownership, view.Expansion, view.Order);

        IReadOnlyList<MountShelfItem> kept = query.Apply(Sample(), wanted: null, prices: null, shuffleSeed: 0);

        Assert.Single(kept);
        Assert.Equal(1u, kept[0].Id);
    }

    [Theory]
    [InlineData("avail:available")]
    [InlineData("avail:a")]
    [InlineData("availability:Available")]
    public void EverySpellingOfTheTermSelectsTheClass(string term) =>
        Assert.True(MountSearchQuery.Parse(term).Matches(Item(1, MountAvailability.Available)));

    [Fact]
    public void AValueThatNamesNoClassStaysFreeText()
    {
        // The grammar's rule for every operator: an unreadable value is not a constraint, so the term cannot empty
        // the list by accident - it matches only a mount whose text really contains it.
        MountSearchQuery query = MountSearchQuery.Parse("avail:banana");

        Assert.False(query.Matches(Item(1, MountAvailability.Available)));
    }

    [Fact]
    public void TheClassAQueryStatesIsReadableBack()
    {
        // Applying a saved view needs this: the preset's query is the authority on the class axis, so the combo's own
        // value must not also apply. Without the readback, "Farmable now" came up empty for a player whose last class
        // filter was Premium.
        Assert.Equal(MountAvailability.Available, MountSearchQuery.AvailabilityOf("avail:available"));
        Assert.Equal(MountAvailability.Premium, MountSearchQuery.AvailabilityOf("source:trial avail:premium"));
        Assert.Equal(MountAvailability.Seasonal, MountSearchQuery.AvailabilityOf("avail:season"));

        Assert.Null(MountSearchQuery.AvailabilityOf(string.Empty));
        Assert.Null(MountSearchQuery.AvailabilityOf("trial"));
        Assert.Null(MountSearchQuery.AvailabilityOf("avail:banana"));
    }

    private static MountQuery Query(MountAvailability? availability) =>
        new(null, MountOwnership.All, MountExpansion.All, MountOrder.Name) { Availability = availability };

    private static IReadOnlyList<MountShelfItem> Sample() =>
    [
        Item(1, MountAvailability.Available, owned: false),
        Item(2, MountAvailability.Available, owned: true),
        Item(3, MountAvailability.Premium, owned: false),
        Item(4, MountAvailability.Unknown, owned: false),
    ];

    private static MountShelfItem Item(uint id, MountAvailability availability, bool owned = false) =>
        new(
            id,
            $"Mount {id}",
            0,
            0,
            1,
            "7.0",
            owned,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            Availability: availability);
}
