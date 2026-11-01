using System;

namespace Mountbook.Services;

/// <summary>
/// The arithmetic that decides how tall the two columns are, kept apart from the ImGui calls that feed it
/// because getting it wrong is invisible until content slides under the fixed footer.
/// </summary>
/// <remarks>
/// The window draws into a <c>Gw.BeginCard()</c> child, and an auto-sized child's content region grows with
/// its content: measured 661-715 logical units inside a 660-tall window, with a clip rect running to 842. A
/// body sized from that number is taller than the pane, so its last rows are clipped by the window instead of
/// scrolling, and the footer is drawn over them. The window's own clip bottom is the honest bottom edge, so
/// the height comes from screen-space geometry and the reported region is only an upper bound.
/// </remarks>
public static class LayoutMeasure
{
    /// <summary>
    /// The height available to the columns: from the cursor's screen position down to the draw list's clip
    /// bottom, less the card's bottom inset. Clamped at 1 so a pane smaller than its own insets still draws,
    /// and capped at <paramref name="reportedPx"/> because that number can be larger than the window.
    /// </summary>
    /// <param name="clipBottomPx">The window draw list's clip-rect bottom, in screen pixels.</param>
    /// <param name="cursorScreenYPx">The cursor's screen Y where the columns start.</param>
    /// <param name="cardBottomInsetPx">The inset <c>Gw.EndCard()</c> reserves under the columns.</param>
    /// <param name="reportedPx">What <c>ImGui.GetContentRegionAvail().Y</c> claims is left.</param>
    public static float ColumnHeight(
        float clipBottomPx,
        float cursorScreenYPx,
        float cardBottomInsetPx,
        float reportedPx)
    {
        float room = clipBottomPx - cursorScreenYPx - cardBottomInsetPx;
        return MathF.Max(MathF.Min(room, reportedPx), 1f);
    }

    /// <summary>
    /// The narrowest detail pane that draws the two-column header, in logical units. The arithmetic floor is
    /// lower - the artwork's 96px minimum, its mat and gutter, and the title column's 170px need ~294 - but at
    /// 330 the title column keeps ~192px, which is where even the longest mount names draw whole instead of
    /// ellipsised. Below it the header reads as a portrait with a sliver of text beside it, which is the shape
    /// the first two-column attempt was reverted for.
    /// </summary>
    public const float DetailTwoColumnMinWidth = 330f;

    /// <summary>
    /// The narrowest detail pane that puts the watch toggle and its threshold stepper on one row, in logical
    /// units.
    /// </summary>
    /// <remarks>
    /// The two-column header's own number, referenced rather than repeated: the two gates happen to share a
    /// threshold, and two constants that must agree are two constants that can drift apart.
    /// </remarks>
    public const float WatchRowMinWidth = DetailTwoColumnMinWidth;

    /// <summary>True when the detail pane is wide enough to put the watch toggle and its stepper on one row.</summary>
    /// <param name="paneWidth">The detail pane's width, in logical units.</param>
    public static bool WatchRowFits(float paneWidth) => paneWidth >= WatchRowMinWidth;

    /// <summary>
    /// The narrowest filter column that draws two controls on one row, in logical units.
    /// </summary>
    /// <remarks>
    /// The filter block used to be seven rows of one control each: 321 of the 505 px the list column has at
    /// the default window size, and the whole column at the window's 820x520 minimum, where the grid then drew
    /// no card at all. Pairing the combos halves it - but only once each field has room for a value that names
    /// its own dimension ("All expansions", "Sort: Name"), which is what the labels used to consume. The
    /// column measures 589 px at the default 1040x660 window, 438 px at the 820x520 minimum and 328 px at the
    /// 620x820 narrow one, so at the narrow size a half of it (160 px) leaves the field 120 px of ink once the
    /// control's padding and chevron come out: enough for the defaults, not for the values a player picks
    /// ("Sort: Commonest first" is 180.9 px, "V&C Dungeon (2)" 198.2). Below this width the pairs stack back
    /// into one column rather than truncate what was chosen; above it the dimension stays visible at the cost of
    /// a clipped tail on the longest value.
    /// </remarks>
    public const float FilterPairMinWidth = 340f;

    /// <summary>True when the filter column is wide enough to draw two controls on one row.</summary>
    /// <param name="columnWidth">The filter column's width, in logical units.</param>
    public static bool FilterPairFits(float columnWidth) => columnWidth >= FilterPairMinWidth;

    /// <summary>
    /// How many of the saved-view chips fit across the filter column, leaving the trailing button that opens the
    /// dialog its corner.
    /// </summary>
    /// <remarks>
    /// The row is one line - the whole point of it is that it costs the list a line and not a block - so a chip
    /// that does not fit is not drawn rather than wrapped: it is still in the dialog, which is where the name is
    /// edited anyway. The trailing button is always drawn, even at zero chips, because a row with nothing on it
    /// is not a row. Pure so the three column widths the layout work measured (589, 438 and 328 logical px) can be
    /// pinned in a test against real label widths.
    /// </remarks>
    /// <param name="columnWidth">The filter column's width, in pixels.</param>
    /// <param name="reserve">What the trailing button takes, in pixels, and the gutter that separates it.</param>
    /// <param name="gap">The gutter between two chips, in pixels.</param>
    /// <param name="widths">Each chip's own width, in draw order, in pixels.</param>
    /// <returns>How many leading chips fit, which may be none.</returns>
    public static int ViewsThatFit(float columnWidth, float reserve, float gap, ReadOnlySpan<float> widths)
    {
        float left = columnWidth - reserve;

        for (var count = 0; count < widths.Length; count++)
        {
            float used = 0f;

            for (var index = 0; index <= count; index++)
            {
                used += widths[index];
            }

            used += gap * (count + 1);

            if (used > left)
            {
                return count;
            }
        }

        return widths.Length;
    }

    /// <summary>
    /// The two-column detail header's measurements, in logical units.
    /// </summary>
    /// <param name="ArtWidth">The artwork box's width; the box is square, so this is also its height.</param>
    /// <param name="TextWidth">The width left beside the artwork for the title, the rarity line, the badges and
    /// the sheet facts.</param>
    /// <param name="TwoColumn">Whether the pane is wide enough to draw the column form at all.</param>
    public readonly record struct DetailHeaderColumns(float ArtWidth, float TextWidth, bool TwoColumn);

    /// <summary>
    /// How wide the artwork and the text column are in the two-column detail header, and whether the pane may
    /// draw it. Pure so the thresholds can be pinned in a test: the failure mode is a title column too narrow
    /// to hold a name, which the goldens only show on the one scene whose mount has a long one.
    /// </summary>
    /// <param name="paneWidth">The detail pane's width, logical units.</param>
    /// <param name="artShare">The artwork's share of the pane before it is clamped.</param>
    /// <param name="artMin">The smallest artwork box, logical units.</param>
    /// <param name="artMax">The largest artwork box, logical units.</param>
    /// <param name="chrome">What the artwork column takes beyond the image: mat on both sides plus the gutter.</param>
    /// <param name="titleMin">The narrowest title column worth drawing the two-column form into.</param>
    /// <param name="titleWidthLogical">The selected mount's name, measured in the title role, in *logical*
    /// units - the same units as every other argument; zero when it is not known yet. A caller that measures
    /// text gets pixels back, so it has to divide by the UI scale first: passing those pixels here made the
    /// artwork give way to a title twice its real width at 200% scale, which no golden could show because
    /// every scene renders at scale 1.</param>
    public static DetailHeaderColumns DetailHeader(
        float paneWidth,
        float artShare,
        float artMin,
        float artMax,
        float chrome,
        float titleMin,
        float titleWidthLogical)
    {
        float art = Math.Clamp(paneWidth * artShare, artMin, artMax);

        // The artwork gives way to the title. A name is the one thing in this header that cannot be
        // abbreviated without losing what the player came for, and a portrait at 96 rather than 112 still
        // reads as a portrait: so the column shrinks to its floor before the title ellipsises. One pixel of
        // slack, because the fit test is a <= against the same measured width.
        if (titleWidthLogical > 0f)
        {
            art = MathF.Min(art, MathF.Max(paneWidth - chrome - titleWidthLogical - 1f, artMin));
        }

        float text = MathF.Max(paneWidth - art - chrome, 1f);

        return new DetailHeaderColumns(art, text, paneWidth >= DetailTwoColumnMinWidth && text >= titleMin);
    }

    /// <summary>
    /// The height the detail pane's fixed footer reserves: one row per action line, the item spacing above,
    /// between and below them, and the disabled-reason line when there is one.
    /// </summary>
    /// <remarks>
    /// Extracted from the view so the arithmetic that once left the footer two pixels short can be pinned:
    /// <paramref name="rowHeightPx"/> has to be <c>Max(Look.ControlHeight, Look.MinHit)</c> at the UI scale,
    /// because <c>Gw.Button</c> reserves its item that way and a control-height-only sum under-reserved the row.
    /// </remarks>
    /// <param name="rowHeightPx">One action row's height, in screen pixels, already scaled.</param>
    /// <param name="rows">How many action lines the footer holds.</param>
    /// <param name="itemSpacingPx">ImGui's item spacing, the gap above, between and under the rows.</param>
    /// <param name="reasonLinePx">The disabled-reason line's height, or zero when no reason is drawn.</param>
    public static float DetailFooter(float rowHeightPx, int rows, float itemSpacingPx, float reasonLinePx)
        => (rowHeightPx * rows) + (itemSpacingPx * (rows + 1)) + reasonLinePx;

    /// <summary>
    /// What the scrolling body gets under that footer, clamped at 1 so a pane shorter than its own footer
    /// still draws something rather than a zero-height child that silently skips its content.
    /// </summary>
    /// <param name="availablePx">The pane's height, in screen pixels.</param>
    /// <param name="footerPx">What <see cref="DetailFooter"/> reserves.</param>
    public static float BodyHeight(float availablePx, float footerPx) => MathF.Max(availablePx - footerPx, 1f);

    /// <summary>
    /// The bottom inset to reserve under the columns: the value measured from the card's own geometry when
    /// one has been taken, and <paramref name="fallbackPx"/> - the padding the card pads itself with - until
    /// then, so the first frame is sized from the same number the measurement then confirms.
    /// </summary>
    /// <param name="measuredPx">A measured inset, or a non-positive value when there is none yet.</param>
    /// <param name="fallbackPx">What to use before the first measurement, in pixels.</param>
    public static float CardInset(float measuredPx, float fallbackPx)
        => measuredPx > 0f ? measuredPx : fallbackPx;
}
