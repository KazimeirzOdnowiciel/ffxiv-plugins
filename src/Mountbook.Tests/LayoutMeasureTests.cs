namespace Mountbook.Tests;

using System;
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
        // The acceptance shape: whatever the body gets, the footer has to fit under it inside the pane. Both
        // halves are the functions the view calls - the footer used to be a hard-coded 44 here and subtracted
        // from the same constant it was supposed to check, so this test could not fail.
        const float paneTopPx = 150f;
        const float clipBottomPx = 660f;
        const int rows = 2;

        float columns = LayoutMeasure.ColumnHeight(clipBottomPx, paneTopPx, 16f, 900f);
        float footer = LayoutMeasure.DetailFooter(
            rowHeightPx: 44f,
            rows,
            itemSpacingPx: 4f,
            reasonLinePx: 20f);
        float body = LayoutMeasure.BodyHeight(columns, footer);

        Assert.Equal(columns, body + footer, 3);
        Assert.True(paneTopPx + body + footer <= clipBottomPx - 16f);
    }

    [Fact]
    public void TheFooterPaysARowPerLineAndASpacingBetweenThem()
    {
        // 44 + 8 with one row, 88 + 12 with two: the spacing is above, between and below, which is the two-pixel
        // shortfall a control-height-only sum left behind.
        Assert.Equal(52f, LayoutMeasure.DetailFooter(44f, 1, 4f, 0f), 3);
        Assert.Equal(100f, LayoutMeasure.DetailFooter(44f, 2, 4f, 0f), 3);

        // The disabled-reason line is only paid for when there is a reason to draw.
        Assert.Equal(72f, LayoutMeasure.DetailFooter(44f, 1, 4f, 20f), 3);
    }

    [Fact]
    public void ABodyShorterThanItsFooterStillDraws()
    {
        Assert.Equal(348f, LayoutMeasure.BodyHeight(400f, 52f), 3);
        Assert.Equal(1f, LayoutMeasure.BodyHeight(40f, 52f), 3);
    }

    [Theory]
    [InlineData(329.9f, false)]
    [InlineData(330f, true)]
    public void TheWatchRowFollowsThePaneWidth(float paneWidth, bool oneRow) =>
        // The gate that puts the watch toggle and its stepper on one line, or stacks them: every other
        // LayoutMeasure gate is pinned, and this one had no test while it decides the detail pane's own row.
        Assert.Equal(oneRow, LayoutMeasure.WatchRowFits(paneWidth));

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

    /// <summary>
    /// The saved-views row is one line of the filter block, so the chips that do not fit are dropped rather than
    /// wrapped - and the trailing button that opens the dialog keeps its corner whatever the chips do.
    /// </summary>
    /// <remarks>
    /// The widths here are a label's *shape* - a chip is its text plus padding - not a measurement: the real ones
    /// come from the Look's type face, and what pins those is the views golden. What this pins is the arithmetic
    /// the row leans on: the reserve is taken out first, every chip pays one gutter, and the answer is a prefix
    /// count rather than a wrap.
    /// </remarks>
    [Fact]
    public void TheSavedViewRowKeepsTheButtonItsCorner()
    {
        float[] five = [40f, 40f, 40f, 40f, 40f];

        // 230 - 70 = 160 px of chips: three of 40 plus three gutters of 8 is 144, a fourth is 192.
        Assert.Equal(3, LayoutMeasure.ViewsThatFit(230f, 70f, 8f, five));

        // A row with no room for even one chip still draws the button.
        Assert.Equal(0, LayoutMeasure.ViewsThatFit(70f, 70f, 8f, five));

        // Everything fits when it fits, and an empty list asks for nothing.
        Assert.Equal(2, LayoutMeasure.ViewsThatFit(230f, 70f, 8f, [40f, 40f]));
        Assert.Equal(0, LayoutMeasure.ViewsThatFit(230f, 70f, 8f, []));

        // A chip that would only fit by sitting under the button is not counted: the reserve is a hard cut, and
        // the button is the only door into the dialog.
        Assert.Equal(0, LayoutMeasure.ViewsThatFit(190f, 150f, 8f, [40f]));
    }

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

    /// <summary>
    /// The four ownership chips, at the label widths the render really measured on the real count tuple
    /// (300 mounts, 233 unowned): "All 300" 31.79, "Owned 67" 47.43, "Unowned 233" 64.53 and "Wanted 12" 50.17 px
    /// at subheadline/semibold, plus the Look's 2 px knob inset and a small space of slack. The twelve-mount
    /// sample the other goldens draw has a widest label of 53.18 px, so those scenes ask for a narrower row.
    /// </summary>
    private static readonly float[] ChipLabelsPx = [31.79f, 47.43f, 64.53f, 50.17f];

    /// <summary>The widest chip label with the twelve-mount sample's counts, in pixels.</summary>
    private const float SampleWidestChipPx = 53.18f;

    /// <summary>The knob inset <c>Gw.Segmented</c> gives a cell's ink on both sides, in pixels.</summary>
    private const float ChipInsetPx = 2f;

    /// <summary>The rounding and hinting slack the row reserves, in pixels.</summary>
    private const float ChipSlackPx = 8f;

    /// <summary>The chip row's requirement for a label set, at the measured constants above.</summary>
    /// <param name="widestLabelPx">The widest label's advance width, in pixels.</param>
    private static float ChipsRow(float widestLabelPx) =>
        LayoutMeasure.ChipsWidth(ChipLabelsPx.Length, widestLabelPx, ChipInsetPx, ChipSlackPx);

    [Fact]
    public void TheChipRowIsTheWidestLabelInFourEqualCells()
    {
        // 4 x ceil(64.53 + 2 x 2) + 8. The ceiling makes every cell a whole pixel wide, and the slot after it is
        // the fraction a semibold face adds over the advance that was measured.
        Assert.Equal(284f, ChipsRow(64.53f), 3);

        // The sample's own labels: four cells of 58 plus the slack.
        Assert.Equal(240f, ChipsRow(SampleWidestChipPx), 3);
    }

    [Fact]
    public void TheChipRowFitsEveryColumnTheGoldensDrawInto()
    {
        // The columns the scenes really have: 328 is the 620-wide narrow squeeze, 452 the 820x520 minimum, 589
        // the default window and 1134 the 1920-wide one. The real labels fit all four, so the row keeps its
        // shape as the window is dragged wider - it is not one size that happens to fit.
        Assert.True(LayoutMeasure.ChipsFit(328f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
        Assert.True(LayoutMeasure.ChipsFit(452f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
        Assert.True(LayoutMeasure.ChipsFit(589f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
        Assert.True(LayoutMeasure.ChipsFit(1134f, 4, 64.53f, ChipInsetPx, ChipSlackPx));

        // Below the requirement the picker draws: 283.9 is one pixel under it, 266 is the chipcombo scene's own
        // column, and neither may draw a chip.
        Assert.False(LayoutMeasure.ChipsFit(283.9f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
        Assert.False(LayoutMeasure.ChipsFit(266f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
    }

    [Fact]
    public void EveryColumnTheChipsDrawIntoHoldsEveryLabelWhole()
    {
        // The acceptance, swept: for every column width from 100 to 1400 px, either the row is measured as not
        // fitting (so the picker draws) or the row is drawn no wider than the column and every one of the four
        // labels has ink left over in its cell. A gate that passed one pixel early shows up here as a negative
        // margin.
        for (var column = 100f; column <= 1400f; column += 1f)
        {
            if (!LayoutMeasure.ChipsFit(column, ChipLabelsPx.Length, 64.53f, ChipInsetPx, ChipSlackPx))
            {
                continue;
            }

            float row = ChipsRow(64.53f);
            float cell = row / ChipLabelsPx.Length;

            Assert.True(column >= row, $"chips drawn into {column} px for a {row} px row");

            foreach (float label in ChipLabelsPx)
            {
                float ink = cell - (ChipInsetPx * 2f);

                Assert.True(ink >= label, $"{label} px label had {ink} px of ink at a {column} px column");
            }
        }
    }

    [Fact]
    public void ALongerTranslationFallsBackToThePicker()
    {
        // The row is measured, so a translation is not a special case: a language whose widest chip is 100 px
        // wide needs 424 and no longer fits the 328 px column of the narrow scene, where the English labels need
        // 284 and do. That is the difference between a measurement and the fixed 380 the row used to switch on -
        // a constant with no relation to the room the control actually had.
        Assert.Equal(424f, ChipsRow(100f), 3);
        Assert.False(LayoutMeasure.ChipsFit(328f, 4, 100f, ChipInsetPx, ChipSlackPx));
        Assert.True(LayoutMeasure.ChipsFit(328f, 4, 64.53f, ChipInsetPx, ChipSlackPx));
    }

    [Fact]
    public void TheChipRowFollowsTheUiScale()
    {
        // Everything here is pixels, so a 150% scale multiplies the labels and the inset together and the
        // comparison stays in one unit. The shape of bug a "380 * scale" gate invites - a scaled constant against
        // a column that was not - cannot be expressed at all.
        const float Scale = 1.5f;
        float widest = 64.53f * Scale;

        Assert.Equal(
            4f * MathF.Ceiling(widest + (ChipInsetPx * Scale * 2f)) + (ChipSlackPx * Scale),
            LayoutMeasure.ChipsWidth(ChipLabelsPx.Length, widest, ChipInsetPx * Scale, ChipSlackPx * Scale),
            3);
        Assert.True(LayoutMeasure.ChipsFit(328f * Scale, 4, widest, ChipInsetPx * Scale, ChipSlackPx * Scale));
        Assert.False(LayoutMeasure.ChipsFit(280f * Scale, 4, widest, ChipInsetPx * Scale, ChipSlackPx * Scale));
    }

    [Fact]
    public void AChipRowOfNoCellsAsksForNothing()
    {
        // The gate is also the guard: a control with no cells cannot clip a label, and ChipsFit must not read a
        // zero-width row as "fits anything".
        Assert.Equal(0f, LayoutMeasure.ChipsWidth(0, 64.53f, ChipInsetPx, ChipSlackPx), 3);
        Assert.False(LayoutMeasure.ChipsFit(1000f, 0, 64.53f, ChipInsetPx, ChipSlackPx));
    }
}
