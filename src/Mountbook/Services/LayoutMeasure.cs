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
    /// <param name="titleWidth">The selected mount's name, measured in the title role; zero when it is not
    /// known yet.</param>
    public static DetailHeaderColumns DetailHeader(
        float paneWidth,
        float artShare,
        float artMin,
        float artMax,
        float chrome,
        float titleMin,
        float titleWidth)
    {
        float art = Math.Clamp(paneWidth * artShare, artMin, artMax);

        // The artwork gives way to the title. A name is the one thing in this header that cannot be
        // abbreviated without losing what the player came for, and a portrait at 96 rather than 112 still
        // reads as a portrait: so the column shrinks to its floor before the title ellipsises. One pixel of
        // slack, because the fit test is a <= against the same measured width.
        if (titleWidth > 0f)
        {
            art = MathF.Min(art, MathF.Max(paneWidth - chrome - titleWidth - 1f, artMin));
        }

        float text = MathF.Max(paneWidth - art - chrome, 1f);

        return new DetailHeaderColumns(art, text, paneWidth >= DetailTwoColumnMinWidth && text >= titleMin);
    }

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
