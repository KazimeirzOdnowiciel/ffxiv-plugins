using System;
using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The availability classification: what class a mount's obtain lines put it in, and the three claims the feature
/// makes about it - one class per mount, four visibly different ways of being unobtainable, and one spelling that
/// the snapshot, the saved filter and the search grammar all read.
/// </summary>
/// <remarks>
/// The rules are asserted type by type on purpose: a new obtain type must be classified deliberately, so the sweep
/// over <see cref="MountSourceKinds.Taxonomy"/> fails until somebody decides what the type means. The curated ids
/// are asserted against the shipped snapshot in <see cref="MountSourcesDataTests"/>.
/// </remarks>
public sealed class MountAvailabilityTests
{
    [Fact]
    public void EveryNonSpecialTypeIsAvailable()
    {
        // The four types that are not "go and play" are the ones the classes are named after; everything else in
        // the taxonomy is obtainable today, and a type added to the taxonomy without a rule lands here.
        var expected = new Dictionary<string, MountAvailability>(StringComparer.Ordinal)
        {
            ["premium"] = MountAvailability.Premium,
            ["event"] = MountAvailability.Seasonal,
            ["pvp"] = MountAvailability.Ranked,
            ["other"] = MountAvailability.Unknown,
        };

        foreach (MountSourceKind kind in MountSourceKinds.Taxonomy)
        {
            MountAvailability actual = MountAvailabilityRules.LineClass(new MountSourceLine(kind, "detail", string.Empty, 0));

            if (expected.TryGetValue(kind.Key, out MountAvailability wanted))
            {
                Assert.Equal(wanted, actual);
                continue;
            }

            Assert.Equal(MountAvailability.Available, actual);
        }
    }

    [Fact]
    public void ATypeThisBuildDoesNotKnowIsUnknownAndNotAvailable()
    {
        MountSourceKind unknown = MountSourceKinds.Parse("Brand New Type");
        MountSourceLine line = new(unknown, "somewhere", string.Empty, 0);

        Assert.False(unknown.IsKnown);
        Assert.Equal(MountAvailability.Unknown, MountAvailabilityRules.LineClass(line));
    }

    [Theory]
    [InlineData("The Feast: Season 3")]
    [InlineData("PvP Series 12 - Level 25")]
    public void APvpSeasonOrSeriesIsRanked(string detail) =>
        Assert.Equal(
            MountAvailability.Ranked,
            MountAvailabilityRules.LineClass(new MountSourceLine("PvP", detail, string.Empty, 0)));

    [Theory]
    [InlineData("20,000 Wolf Marks")]
    [InlineData("5,000 Trophy Crystals")]
    public void PvpCurrencyIsARouteThatIsOpenNow(string detail) =>
        Assert.Equal(
            MountAvailability.Available,
            MountAvailabilityRules.LineClass(new MountSourceLine("PvP", detail, string.Empty, 0)));

    [Fact]
    public void AMountWithNoObtainLinesIsUnknownRatherThanAvailable()
    {
        // 94 of the game's Mount rows are mounts the site has never listed. "Nothing recorded" must never read as
        // "you can farm it", which is the one wrong answer that would send a player looking for a mount that has no
        // route at all.
        Assert.Equal(MountAvailability.Unknown, MountAvailabilityRules.Classify(430, []));
    }

    [Fact]
    public void AnOpenRouteWinsOverAClosedOne()
    {
        // The real shape this decides: a former PvP series reward that the crystal vendor stocks now. The player's
        // question is "can I get it", and one open route answers it.
        MountShelfItem item = Item(
            "PvP", "PvP Series 3 - Level 15",
            "PvP", "5,000 Trophy Crystals");

        Assert.Equal(MountAvailability.Available, MountAvailabilityRules.Classify(item.Id, item.Sources));
    }

    [Fact]
    public void MoneyWinsOverOutOfSeason()
    {
        // A 2015 event mount that the online store sells: the event is over, the store is not.
        MountShelfItem item = Item("Event", "All Saints' Wake (2015)", "Premium", "Online Store");

        Assert.Equal(MountAvailability.Premium, MountAvailabilityRules.Classify(item.Id, item.Sources));
    }

    [Fact]
    public void SeasonalWinsOverRanked()
    {
        MountShelfItem item = Item("PvP", "The Feast: Season 7", "Event", "Moogle Treasure Trove Event");

        Assert.Equal(MountAvailability.Seasonal, MountAvailabilityRules.Classify(item.Id, item.Sources));
    }

    [Fact]
    public void CurationWinsOverTheTypeRules()
    {
        // Both 1.x rewards are filed as "Other", which the rules deliberately cannot place: the curated list is
        // where a person wrote down what they are. Id 5's own line is the campaign text, and id 4's names a 1.0
        // NPC - neither is a route the game still has.
        foreach (uint id in MountAvailabilityRules.CuratedIds)
        {
            MountShelfItem item = Item(MountSourceKinds.Parse("Other"), "Legacy Campaign");

            Assert.Equal(MountAvailability.Legacy, MountAvailabilityRules.Classify(id, item.Sources));
        }
    }

    [Fact]
    public void AnUnknownTypeIsLeftUnknownForTheValidatorToRefuse()
    {
        // The difference the validator acts on: a row with *no* lines is Unknown (a fact about the dataset, drawn
        // as "No data"), while a row with lines this build cannot place is a rule nobody has taught yet - which is
        // an error at patch time rather than a class. Both read Unknown here, and the lines are what tell them
        // apart, which is why the classifier refuses on lines.Count > 0 and not on the class alone.
        MountShelfItem item = Item(MountSourceKinds.Parse("Brand New Type"), "somewhere");

        Assert.Single(item.Sources);
        Assert.Equal(MountAvailability.Unknown, MountAvailabilityRules.Classify(item.Id, item.Sources));
    }

    [Fact]
    public void TheFourUnobtainableClassesHaveFourDifferentTones()
    {
        // "Visibly distinct" as a test: five classes, five tones, and only Unknown is untinted. A map that folded
        // two classes onto one colour would pass every other assertion here and still leave a player unable to tell
        // a seasonal mount from a retired one.
        var tones = new HashSet<MountAvailabilityTone>();

        foreach (MountAvailability availability in new[]
                 {
                     MountAvailability.Available,
                     MountAvailability.Seasonal,
                     MountAvailability.Ranked,
                     MountAvailability.Premium,
                     MountAvailability.Legacy,
                 })
        {
            MountAvailabilityTone tone = MountAvailabilityRules.ToneOf(availability);

            Assert.NotEqual(MountAvailabilityTone.None, tone);
            Assert.True(tones.Add(tone), $"{availability} reuses the tone {tone}.");
        }

        Assert.Equal(MountAvailabilityTone.None, MountAvailabilityRules.ToneOf(MountAvailability.Unknown));
    }

    [Fact]
    public void EveryClassRoundTripsThroughItsKey()
    {
        foreach (MountAvailability availability in MountAvailabilityRules.Classes)
        {
            string key = MountAvailabilityRules.Key(availability);

            Assert.Equal(availability, MountAvailabilityRules.Parse(key));
            Assert.Equal(availability, MountAvailabilityRules.Parse(key.ToUpperInvariant()));
            Assert.Equal(availability, MountAvailabilityRules.Parse("  " + key + "  "));
        }
    }

    [Fact]
    public void AKeyOfAnotherClassPrefixesToThatClassOnlyAsFarAsItIsUnambiguous()
    {
        // Every key starts with a different letter, so the shortening a search box needs works down to one
        // character - and a word that is not a class refuses rather than guessing.
        Assert.True(MountAvailabilityRules.TryParseOperator("a", out MountAvailability available));
        Assert.Equal(MountAvailability.Available, available);

        Assert.True(MountAvailabilityRules.TryParseOperator("p", out MountAvailability premium));
        Assert.Equal(MountAvailability.Premium, premium);

        Assert.True(MountAvailabilityRules.TryParseOperator("season", out MountAvailability seasonal));
        Assert.Equal(MountAvailability.Seasonal, seasonal);

        Assert.False(MountAvailabilityRules.TryParseOperator("q", out _));
        Assert.False(MountAvailabilityRules.TryParseOperator(string.Empty, out _));
        Assert.False(MountAvailabilityRules.TryParseOperator("   ", out _));
    }

    [Fact]
    public void RubbishFromAFileIsUnknownAndToldApartFromAChosenUnknown()
    {
        // The saved filter needs both answers: Parse says "some class, even if it is Unknown" for the snapshot
        // field, TryParseKey says whether the text named one at all so the config can clear a hand edit.
        Assert.Equal(MountAvailability.Unknown, MountAvailabilityRules.Parse("banana"));
        Assert.False(MountAvailabilityRules.TryParseKey("banana", out _));

        Assert.True(MountAvailabilityRules.TryParseKey("unknown", out MountAvailability chosen));
        Assert.Equal(MountAvailability.Unknown, chosen);
    }

    [Fact]
    public void TheSearchTermIsTheGrammarTheSeedWrites()
    {
        Assert.Equal("avail:available", MountAvailabilityRules.SearchTerm(MountAvailability.Available));
        Assert.Equal("avail:premium", MountAvailabilityRules.SearchTerm(MountAvailability.Premium));
    }

    [Fact]
    public void TheTallyCountsMountsAndOnlyListsClassesTheShelfCarries()
    {
        MountShelfItem[] items =
        [
            Item(MountAvailability.Available),
            Item(MountAvailability.Available),
            Item(MountAvailability.Premium),
            Item(MountAvailability.Unknown),
        ];

        IReadOnlyList<MountAvailabilityCount> tally = MountAvailabilityRules.Tally(items);

        // Classes order, not insertion order, and no entry for a class nothing is in: the combo must not offer a
        // filter that selects nothing.
        Assert.Equal(
            [MountAvailability.Available, MountAvailability.Premium, MountAvailability.Unknown],
            [.. Select(tally)]);

        Assert.Equal(2, tally[0].Mounts);
        Assert.Equal(1, tally[1].Mounts);
        Assert.Equal(1, tally[2].Mounts);
        Assert.Equal(2, MountAvailabilityRules.CountOf(items, MountAvailability.Available));
        Assert.Equal(0, MountAvailabilityRules.CountOf(items, MountAvailability.Legacy));
    }

    [Fact]
    public void TheExplanationCarriesTheClassCountAndTheShelfTotal()
    {
        string line = MountAvailabilityRules.Explain(MountAvailability.Premium, 67, 356);

        Assert.Contains("67", line, StringComparison.Ordinal);
        Assert.Contains("356", line, StringComparison.Ordinal);
    }

    private static MountAvailability[] Select(IReadOnlyList<MountAvailabilityCount> tally)
    {
        var classes = new MountAvailability[tally.Count];

        for (var index = 0; index < tally.Count; index++)
        {
            classes[index] = tally[index].Class;
        }

        return classes;
    }

    /// <summary>
    /// One mount carrying <paramref name="availability"/> and no lines, for the tally and filter tests.
    /// </summary>
    private static MountShelfItem Item(MountAvailability availability) =>
        new(
            1,
            "A mount",
            0,
            0,
            1,
            "7.0",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            Availability: availability);

    /// <summary>One mount whose obtain lines are the given category/detail pairs, in order.</summary>
    private static MountShelfItem Item(params string[] categoryAndDetail)
    {
        var lines = new List<MountSourceLine>(categoryAndDetail.Length / 2);

        for (var index = 0; index + 1 < categoryAndDetail.Length; index += 2)
        {
            lines.Add(new MountSourceLine(categoryAndDetail[index], categoryAndDetail[index + 1], string.Empty, 0));
        }

        return new MountShelfItem(
            2,
            "A mount",
            0,
            0,
            1,
            "7.0",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            lines);
    }

    /// <summary>One mount whose only obtain line is of <paramref name="kind"/>.</summary>
    private static MountShelfItem Item(MountSourceKind kind, string detail) =>
        new(
            3,
            "A mount",
            0,
            0,
            1,
            "7.0",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [new MountSourceLine(kind, detail, string.Empty, 0)]);
}
