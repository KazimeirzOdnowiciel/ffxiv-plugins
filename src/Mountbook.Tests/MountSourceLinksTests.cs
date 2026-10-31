using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// A "How to obtain" row becomes a link only when the dataset says it names a real record. The rule is
/// the dataset's own: <c>relatedType</c>/<c>relatedId</c> are attached to the 140 rows that name a duty,
/// quest or achievement and left empty on the 283 prose rows, so a link follows the same split.
/// </summary>
public sealed class MountSourceLinksTests
{
    [Theory]
    [InlineData("Quest", "My Little Chocobo (Immortal Flames)")]
    [InlineData("Instance", "The Bowl of Embers (Extreme)")]
    [InlineData("Achievement", "A Legend for All Time")]
    public void ARecordRowLinksToItsWikiPage(string kind, string detail)
    {
        Assert.Equal(
            MountWiki.Url(detail),
            MountSourceLinks.Url(Line(kind, detail, 42u)));
    }

    [Fact]
    public void AnApostropheIsEscapedTheSameWayTheMountLinkDoes()
    {
        // One escaping rule for the whole plugin: two ways to spell a wiki title would produce one working
        // link and one 404 for the same page.
        Assert.Equal(
            "https://ffxiv.gamerescape.com/wiki/Everybody%27s_Darling",
            MountSourceLinks.Url(Line("Achievement", "Everybody's Darling", 862u)));
    }

    [Fact]
    public void ProseWithoutARecordIsNotALink()
    {
        // 283 of 423 rows are sentences like this; a link on them would land on a missing page.
        const string Prose = "At level 30 or above, speak with the Wandering Minstrel in Eastern La Noscea";

        Assert.Null(MountSourceLinks.Url(Line(string.Empty, Prose, 0u)));
        Assert.False(MountSourceLinks.CanLink(Line(string.Empty, Prose, 0u)));
    }

    [Fact]
    public void AnUnknownKindIsNotALinkEvenWithAnId()
    {
        // The dataset only uses the three known kinds; anything else is a shape this build cannot honour.
        Assert.Null(MountSourceLinks.Url(Line("FATE", "Some FATE", 12u)));
        Assert.False(MountSourceLinks.CanLink(Line("FATE", "Some FATE", 12u)));
    }

    [Fact]
    public void ARecordWithNoNameIsNotALink()
    {
        Assert.Null(MountSourceLinks.Url(Line("Quest", "   ", 42u)));
    }

    [Fact]
    public void AKnownKindWithoutAnIdIsNotALink()
    {
        // Half the dataset fields are optional: an id of 0 means the row never pointed at a record.
        Assert.Null(MountSourceLinks.Url(Line("Quest", "A quest name", 0u)));
    }

    [Fact]
    public void TheKindIsComparedExactly()
    {
        // Case matters on purpose: the dataset writes "Quest", and a loose comparison would start linking
        // a kind nobody validated.
        Assert.Null(MountSourceLinks.Url(Line("quest", "A quest name", 42u)));
    }

    private static MountSourceLine Line(string kind, string detail, uint id) => new(kind, detail, kind, id);
}
