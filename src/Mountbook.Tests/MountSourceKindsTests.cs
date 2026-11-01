using System;
using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The obtain taxonomy itself: the 28 types, the fold that gives each a key, and the search grammar's reader.
/// </summary>
/// <remarks>
/// Every type is asserted one by one rather than through a sample, because the taxonomy is a contract with the
/// dataset: a type that stops parsing, or two that fold to one key, would break the filter, the counts and the
/// grouping for that type alone - and a sample would still pass.
/// </remarks>
public sealed class MountSourceKindsTests
{
    /// <summary>
    /// The site's own classification, in its own order (largest group first). Pinned as text on purpose: the
    /// order is what the combo and the source headings draw, so a reorder is a decision to make here first.
    /// </summary>
    private static readonly string[] Expected =
    [
        "Premium",
        "Achievement",
        "Trial",
        "Quest",
        "PvP",
        "Cosmic Exploration",
        "Event",
        "Wondrous Tails",
        "Tribal",
        "Raid",
        "Occult Crescent",
        "Island Sanctuary",
        "Gold Saucer",
        "V&C Dungeon",
        "Deep Dungeon",
        "Skybuilders",
        "FATE",
        "Treasure Hunt",
        "Hunts",
        "Purchase",
        "Chaotic Raid",
        "Bozja",
        "Eureka",
        "Gathering",
        "Crafting",
        "Other",
        "Dungeon",
        "Voyages",
    ];

    public static TheoryData<string> Types
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (string name in Expected)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Fact]
    public void TheTaxonomyIsTheSitesTwentyEightTypesInItsOwnOrder()
    {
        string[] names = [.. MountSourceKinds.Taxonomy.Select(kind => kind.Name)];
        int[] ranks = [.. MountSourceKinds.Taxonomy.Select(kind => kind.Rank)];

        Assert.Equal(28, MountSourceKinds.Taxonomy.Count);
        Assert.Equal(Expected, names);
        Assert.Equal(Enumerable.Range(0, 28), ranks);
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void EveryTypeParsesToItselfFromAnyOfItsSpellings(string name)
    {
        MountSourceKind fromName = MountSourceKinds.Parse(name);

        Assert.True(fromName.IsKnown, $"\"{name}\" is not in the taxonomy.");
        Assert.Equal(name, fromName.Name);
        Assert.Equal(MountSourceKinds.Key(name), fromName.Key);
        Assert.Same(MountSourceKinds.Taxonomy[fromName.Rank], fromName);

        // The three spellings a dataset, a config file and a search term use are one type.
        Assert.Equal(fromName, MountSourceKinds.Parse(name.ToUpperInvariant()));
        Assert.Equal(fromName, MountSourceKinds.Parse(fromName.Key));
        Assert.Equal(fromName, MountSourceKinds.Parse($"  {name}  "));
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void EveryTypeIsReachableFromTheSearchBoxByItsKeyOrItsBeginning(string name)
    {
        MountSourceKind expected = MountSourceKinds.Parse(name);

        Assert.True(MountSourceKinds.TryParseOperator(expected.Key, out MountSourceKind byKey));
        Assert.Same(expected, byKey);

        // A beginning of the key is how "cosmic" reaches a type whose name carries a space, from a search box
        // that splits on spaces - and it resolves only while the text names one type.
        string prefix = expected.Key[..3];
        bool shared = MountSourceKinds.Taxonomy.Any(other => other != expected && other.Key.StartsWith(prefix, StringComparison.Ordinal));

        Assert.Equal(!shared, MountSourceKinds.TryParseOperator(prefix, out MountSourceKind byPrefix));

        if (!shared)
        {
            Assert.Same(expected, byPrefix);
        }
    }

    [Fact]
    public void TwoTypesNeverShareAKey()
    {
        string[] keys = [.. MountSourceKinds.Taxonomy.Select(kind => kind.Key)];

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Contains("v-c-dungeon", keys);
        Assert.Contains("pvp", keys);
    }

    [Theory]
    [InlineData("pvp")]
    [InlineData("PVP")]
    [InlineData(" PvP ")]
    [InlineData("v&c dungeon")]
    [InlineData("v-c-dungeon")]
    public void TheFoldIgnoresCaseWhitespaceAndPunctuation(string spelling)
    {
        MountSourceKind kind = MountSourceKinds.Parse(spelling);

        Assert.True(kind.IsKnown, $"\"{spelling}\" did not resolve to a type.");
    }

    [Fact]
    public void TheFoldFoldsEveryRunOfPunctuationToOneHyphen()
    {
        Assert.Equal("v-c-dungeon", MountSourceKinds.Key("V&C Dungeon"));
        Assert.Equal("v-c-dungeon", MountSourceKinds.Key("v  &  c   dungeon"));
        Assert.Equal("cosmic-exploration", MountSourceKinds.Key("Cosmic Exploration"));
        Assert.Equal("pvp", MountSourceKinds.Key("PvP"));
        Assert.Equal(string.Empty, MountSourceKinds.Key("  "));
    }

    [Fact]
    public void ATypeThisBuildDoesNotKnowKeepsItsOwnSpelling()
    {
        // A snapshot written for a newer patch still filters and groups by its new type - it just sorts last and
        // is what the patch-day validator refuses, so a new type is adopted deliberately rather than by accident.
        MountSourceKind kind = MountSourceKinds.Parse("Brand New Type");

        Assert.False(kind.IsKnown);
        Assert.False(kind.IsAny);
        Assert.Equal("Brand New Type", kind.Name);
        Assert.Equal("brand-new-type", kind.Key);
        Assert.Equal(MountSourceKinds.Unranked, kind.Rank);
    }

    [Fact]
    public void AnUnknownTypesIdentityIsItsKeyRatherThanItsSpelling()
    {
        // A saved filter may spell a type the way the fold does; the mount the dataset spells differently is the
        // same type, so the two have to compare equal (and hash alike, or the tally would count it twice).
        MountSourceKind fromDataset = MountSourceKinds.Parse("Brand New Type");
        MountSourceKind fromFilter = MountSourceKinds.Parse("brand-new-type");

        Assert.NotEqual(fromDataset.Name, fromFilter.Name);
        Assert.Equal(fromDataset, fromFilter);
        Assert.Equal(fromDataset.GetHashCode(), fromFilter.GetHashCode());
        Assert.NotEqual(fromDataset, MountSourceKinds.Parse("Another New Type"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingReadsAsAny(string spelling)
    {
        Assert.True(MountSourceKinds.Parse(spelling).IsAny);
        Assert.False(MountSourceKinds.Parse(spelling).IsKnown);
    }

    [Theory]
    [InlineData("trib", "Tribal")]
    [InlineData("cosmic", "Cosmic Exploration")]
    [InlineData("sky", "Skybuilders")]
    [InlineData("deep", "Deep Dungeon")]
    [InlineData("wondrous", "Wondrous Tails")]
    [InlineData("eurek", "Eureka")]
    public void TheOperatorAcceptsABeginningWhenItNamesOneType(string text, string expected)
    {
        Assert.True(MountSourceKinds.TryParseOperator(text, out MountSourceKind kind), $"\"{text}\" named no type.");
        Assert.Equal(expected, kind.Name);
    }

    [Theory]
    [InlineData("p")]
    [InlineData("d")]
    [InlineData("v")]
    [InlineData("brandnew")]
    [InlineData("")]
    [InlineData(null)]
    public void TheOperatorRefusesWhatNamesNoSingleType(string text)
    {
        // "p" is Premium, Purchase and PvP; a guess would filter to a type nobody asked for, so the term stays
        // free text instead - the same way an unreadable value of any other operator does.
        Assert.False(MountSourceKinds.TryParseOperator(text, out _));
    }

    [Fact]
    public void OfIsDistinctAndKeepsTheDatasetsLineOrder()
    {
        MountShelfItem item = new(
            1,
            "Both",
            0,
            0,
            1,
            "7.0",
            true,
            string.Empty,
            string.Empty,
            string.Empty,
            [
                new MountSourceLine("Trial", "first", string.Empty, 0),
                new MountSourceLine("Achievement", "second", string.Empty, 0),
                new MountSourceLine("trial", "third", string.Empty, 0),
                new MountSourceLine(string.Empty, "no type", string.Empty, 0),
            ]);

        IReadOnlyList<MountSourceKind> kinds = MountSourceKinds.Of(item);

        Assert.Equal(["Trial", "Achievement"], [.. kinds.Select(kind => kind.Name)]);
    }
}
