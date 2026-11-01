namespace Mountbook.Tests;

using Mountbook.Services;
using Xunit;

/// <summary>
/// The column height is the one number that decides whether content scrolls or hides under the footer, and
/// it is computed from screen-space geometry because the reported content region lies inside an auto-sized
/// card. These cases pin the arithmetic, including the shapes that were measured in the preview harness.
/// </summary>
public sealed class LayoutMeasureTests
{
    [Fact]
    public void ClipBottomWinsOverTheInflatedReportedRegion()
    {
        // The harness shape that started this: a 660-tall window whose card reports 715 units of room.
        float height = LayoutMeasure.ColumnHeight(
            clipBottomPx: 660f,
            cursorScreenYPx: 150f,
            cardBottomInsetPx: 16f,
            reportedPx: 715f);

        Assert.Equal(494f, height, 3);
    }

    [Fact]
    public void ReportedRegionWinsWhenItIsTheSmallerNumber()
    {
        float height = LayoutMeasure.ColumnHeight(
            clipBottomPx: 1000f,
            cursorScreenYPx: 100f,
            cardBottomInsetPx: 16f,
            reportedPx: 300f);

        Assert.Equal(300f, height, 3);
    }

    [Fact]
    public void NeverDropsBelowOnePixel()
    {
        // A pane shorter than its own insets must still draw: a zero-height child silently skips its content
        // and the window then looks empty rather than cramped.
        float height = LayoutMeasure.ColumnHeight(
            clipBottomPx: 100f,
            cursorScreenYPx: 120f,
            cardBottomInsetPx: 16f,
            reportedPx: 50f);

        Assert.Equal(1f, height, 3);
    }

    [Fact]
    public void BodyPlusFooterNeverExceedsThePane()
    {
        // The acceptance shape: whatever the body gets, the footer has to fit under it inside the pane.
        const float paneTopPx = 150f;
        const float clipBottomPx = 660f;
        const float footerPx = 44f;

        float columns = LayoutMeasure.ColumnHeight(clipBottomPx, paneTopPx, 16f, 900f);
        float body = columns - footerPx;

        Assert.True(paneTopPx + body + footerPx <= clipBottomPx - 16f);
    }

    /// <summary>
    /// The two-column detail header, at the pane widths the goldens really draw: 211/287 are the two stacked
    /// scenes (narrow and minimum), 332.88 is the sources scene - the narrowest pane that gets the column form -
    /// and 705.28 is wide. Pinned to measured numbers so a change to the art bounds or the gate has to say so.
    /// </summary>
    private static LayoutMeasure.DetailHeaderColumns Header(float paneWidth, float titleWidth)
        => LayoutMeasure.DetailHeader(
            paneWidth,
            artShare: 0.32f,
            artMin: 96f,
            artMax: 112f,
            chrome: 32f,
            titleMin: 170f,
            titleWidth);

    [Theory]
    [InlineData(211.28f)]
    [InlineData(287.28f)]
    public void NarrowPaneKeepsTheStackedHeader(float paneWidth)
    {
        // The acceptance for the narrow and minimum goldens: the two-column form must not reach them, whatever
        // the artwork and the title are.
        Assert.False(Header(paneWidth, titleWidth: 0f).TwoColumn);
        Assert.False(Header(paneWidth, titleWidth: 201.38f).TwoColumn);
    }

    [Theory]
    [InlineData(332.88f)]
    [InlineData(370.88f)]
    [InlineData(705.28f)]
    public void TwoColumnPaneKeepsTheTitleColumn(float paneWidth)
    {
        LayoutMeasure.DetailHeaderColumns columns = Header(paneWidth, titleWidth: 0f);

        Assert.True(columns.TwoColumn);
        Assert.True(columns.TextWidth >= 170f, $"title column was {columns.TextWidth}");
        Assert.InRange(columns.ArtWidth, 96f, 112f);
    }

    [Fact]
    public void TheGateSitsWhereTheTitleColumnStopsBeingWorthIt()
    {
        Assert.False(Header(329.9f, titleWidth: 0f).TwoColumn);
        Assert.True(Header(LayoutMeasure.DetailTwoColumnMinWidth, titleWidth: 0f).TwoColumn);
        Assert.True(Header(LayoutMeasure.DetailTwoColumnMinWidth, titleWidth: 0f).TextWidth >= 170f);
    }

    [Fact]
    public void TheArtworkGivesWayToALongTitle()
    {
        // Measured in the title role at the default Look: "Auspicious Kamuy" is 189.5 logical px and "Genie of
        // the Lamp" is 201.4. On the default window's 343px pane - the detail body with its scrollbar showing -
        // the preferred 109.8px portrait left the column 0.1px short of the long name and it ellipsised; the
        // portrait shrinks instead, and a short name keeps the full artwork.
        LayoutMeasure.DetailHeaderColumns shortName = Header(343f, titleWidth: 189.54f);
        LayoutMeasure.DetailHeaderColumns longName = Header(343f, titleWidth: 201.38f);

        Assert.Equal(109.76f, shortName.ArtWidth, 2);
        Assert.True(longName.ArtWidth < shortName.ArtWidth);
        Assert.True(longName.ArtWidth >= 96f);
        Assert.True(longName.TextWidth >= 201.38f, $"title column was {longName.TextWidth}");
    }

    [Fact]
    public void APaneTooNarrowForTheTitleStillStacks()
    {
        // A name wider than the column can ever be gets ellipsised (nothing else is honest), but it must not
        // take the header to a sliver: the title column still holds its minimum.
        LayoutMeasure.DetailHeaderColumns columns = Header(330f, titleWidth: 400f);

        Assert.True(columns.TwoColumn);
        Assert.True(columns.TextWidth >= 170f);
        Assert.Equal(96f, columns.ArtWidth, 2);
    }

    [Fact]
    public void MeasuredInsetWins()
    {
        // The live window is the only place the inset can be read, and a scaled UI makes it something other
        // than the 16 the goldens see: once measured, that number is what the columns are sized from.
        Assert.Equal(32f, LayoutMeasure.CardInset(measuredPx: 32f, fallbackPx: 16f), 3);
    }

    [Fact]
    public void NoMeasurementYetFallsBackToTheCardsOwnPadding()
    {
        // Frame one draws before anything has been measured. The fallback is the card's padding rather than
        // zero, so the first frame cannot overrun by the whole inset and flash a scrollbar.
        Assert.Equal(16f, LayoutMeasure.CardInset(measuredPx: -1f, fallbackPx: 16f), 3);
        Assert.Equal(16f, LayoutMeasure.CardInset(measuredPx: 0f, fallbackPx: 16f), 3);
    }

    [Fact]
    public void TheFallbackFollowsTheUiScale()
    {
        // 16 was only right at scale 1: the same Look at 150% pads the card by 24 px, and a constant would
        // leave the columns 8 px too tall.
        Assert.Equal(24f, LayoutMeasure.CardInset(measuredPx: 0f, fallbackPx: 16f * 1.5f), 3);
    }

    /// <summary>
    /// The filter column's pairs, at the widths the goldens really draw: 328 is the narrow window (620x820,
    /// where the combos stack), 438 the window's 820x520 minimum and 589 the default 1040x660 (both paired).
    /// The three numbers are measured from the render, not derived - the column is 62% of the card's content
    /// width less the gutter - so a change to that share has to say so here.
    /// </summary>
    /// <param name="columnWidth">The filter column's width, in logical units.</param>
    /// <param name="paired">Whether two controls fit on one row at that width.</param>
    [Theory]
    [InlineData(328f, false)]
    [InlineData(438f, true)]
    [InlineData(589f, true)]
    public void TheFilterPairsFollowTheColumnWidth(float columnWidth, bool paired) =>
        Assert.Equal(paired, LayoutMeasure.FilterPairFits(columnWidth));

    [Fact]
    public void ThePairGateSitsBetweenTheNarrowAndTheMinimumColumn()
    {
        // Below it the narrow window stacks (a half-width field there holds the defaults but none of the
        // values a player picks), above it the minimum window pairs - which is what puts a card row back in
        // the 820x520 golden.
        Assert.False(LayoutMeasure.FilterPairFits(LayoutMeasure.FilterPairMinWidth - 0.1f));
        Assert.True(LayoutMeasure.FilterPairFits(LayoutMeasure.FilterPairMinWidth));
        Assert.InRange(LayoutMeasure.FilterPairMinWidth, 328f, 438f);
    }
}
