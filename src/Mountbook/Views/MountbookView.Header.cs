using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>Part of <see cref="MountbookView"/>: header drawing.</summary>
public sealed partial class MountbookView
{
    /// <summary>
    /// Settings / Refresh / Copy missing, right-aligned in the header. Small and plain (text
    /// ink, no fill) so they read as window chrome rather than competing with Ride.
    /// </summary>
    private static void DrawHeaderActions(
        MountViewState state,
        float left,
        float top,
        Look look,
        float scale)
    {
        // Right-aligned on the card's edge: a hovered action grows past it without this. Paired in a
        // finally so a throw cannot leave the clip stack short (see PushControlRoom).
        PushControlRoom();

        try
        {
            // Themes is not here: it opened the same folder as Settings > Appearance > Open themes
            // folder, and a second door to one folder is 70 px of chrome for nothing.
            Span<string> labels = [Loc.T("header.settings"), Loc.T("header.refresh"), Loc.T("header.copyMissing")];
            Span<string> hints =
            [
                Loc.T("header.settings.hint"),
                Loc.T("header.refresh.hint"),
                Loc.T("header.copyMissing.hint"),
            ];
            var gap = look.SpaceSmall * scale;
            var x = left;

            var widths = ActionWidths(look, scale);

            for (var index = 0; index < labels.Length; index++)
            {
                var label = labels[index];
                var buttonWidth = widths[index];

                ImGui.SetCursorScreenPos(new Vector2(x, top));

                var pressed = Gw.Button(label, ButtonStyle.Plain, ButtonSize.Small);

                Gw.TooltipFor(ImGui.IsItemHovered(), hints[index]);

                if (pressed)
                {
                    switch (index)
                    {
                        case 0:
                            state.SettingsRequested = true;
                            break;
                        case 1:
                            state.RefreshRequested = true;
                            break;
                        default:
                            state.CopyMissingRequested = true;
                            break;
                    }
                }

                x += buttonWidth + gap;
            }
        }
        finally
        {
            PopControlRoom();
        }
    }

    /// <summary>The three header actions' label widths, measured once per UI scale.</summary>
    private static float[] ActionWidths(Look look, float scale)
    {
        if (actionWidthsScale == scale && actionWidths.Length == 3)
        {
            return actionWidths;
        }

        Span<string> labels = [Loc.T("header.settings"), Loc.T("header.refresh"), Loc.T("header.copyMissing")];
        actionWidths = new float[3];
        actionWidthsScale = scale;

        for (var index = 0; index < labels.Length; index++)
        {
            // The same formula as the rest of the header (and the detail pane): a small button reserves its
            // label at footnote/semibold, one medium space each side, never narrower than the control height.
            // It used to measure the callout role here, which is larger, so every button was followed by a
            // few pixels more than the gap it drew with.
            actionWidths[index] = SmallButtonWidth(labels[index], look, scale);
        }

        return actionWidths;
    }

    /// <summary>The width the three header actions need, or zero when there are none to draw.</summary>
    private static float ActionsWidth(Look look, float scale)
    {
        var widths = ActionWidths(look, scale);
        // Two gaps between three buttons, matching the loop in DrawHeaderActions.
        var total = 2f * look.SpaceSmall * scale;

        for (var index = 0; index < widths.Length; index++)
        {
            total += widths[index];
        }

        return total;
    }

    /// <summary>Cached label widths, and the scale they were measured at.</summary>
    private static float[] actionWidths = [];

    /// <summary>The style <c>Gw.Button(ButtonSize.Small)</c> draws its label in: footnote at semibold.</summary>
    private static readonly TextStyle ActionLabelStyle = new(TextRole.Footnote, FontWeight.Semibold);

    private static float actionWidthsScale = -1f;

    private void DrawHeader(MountViewState state)
    {
        RefreshCounts(state);

        var scale = Frame.Scale;
        var look = Frame.Look;

        // Gw.ProgressRing takes its size and stroke in logical units and scales them itself, so passing
        // a pre-scaled value drew the ring scale-squared wide while every cursor expression below
        // reserved only scale times 46 px: at 150% the ring grew over the collected/missing lines.
        const float RingSize = 46f;
        const float RingStroke = 5f;
        var ringPx = RingSize * scale;
        var gap = look.SpaceMedium * scale;

        Gw.Heading(Loc.T("window.main"));

        var at = ImGui.GetCursorScreenPos();
        var contentRight = at.X + ImGui.GetContentRegionAvail().X;
        var textLeft = at.X + ringPx + gap;

        // Measure the text block first: the actions and the strip both have to clear it.
        var firstLine = look.Type.LineHeight(TextRole.Subheadline) * scale;
        var secondLine = look.Type.LineHeight(TextRole.Footnote) * scale;
        var stackGap = look.SpaceExtraSmall * scale;
        // Cached in RefreshCounts: these strings only change when the numbers do.
        var countWidth = countWidthCache;
        var remainingWidth = remainingWidthCache;

        // Where the second line of statistics can still reach: the header actions own the right edge of
        // the first line, and a strip or a sentence that ran under them would be chrome over chrome.
        var actionsClaim = ActionsWidth(look, scale);
        var actionsLeft = contentRight - actionsClaim;
        var lineRight = actionsLeft - (look.SpaceSmall * scale);
        var stripLeft = textLeft + remainingWidth + (look.SpaceLarge * scale);
        var stripWidth = StripWidth(state, look, scale);
        var stripInline = stripWidth > 0f && stripLeft + stripWidth < lineRight;

        // The rarest mount still missing sits beside the count it qualifies, on the counters' own line: the
        // second line is already spoken for by "n still missing" and the expansion strip, and a third line
        // would cost the shelf a row of cards for one sentence. It is drawn only when the words fit - at the
        // narrow sizes they do not - and what does not fit is still said by the ring's tooltip, which is why
        // the sentence is worked out before the ring is drawn.
        var rarestLeft = textLeft + countWidth + (look.SpaceLarge * scale);
        RefillRarest(state.RarestMissing);
        var rarestLine = RarestLine(lineRight - rarestLeft);

        // The ring carries the percentage...
        ImGui.SetCursorScreenPos(at);
        Gw.ProgressRing(Fraction(state.Owned, state.Total), RingSize, RingStroke, look.Accent);

        var percentWidth = percentWidthCache;
        var percentLine = look.Type.LineHeight(TextRole.Footnote) * scale;

        ImGui.SetCursorScreenPos(new Vector2(
            at.X + ((ringPx - percentWidth) * 0.5f),
            at.Y + ((ringPx - percentLine) * 0.5f)));
        Gw.Text(percentText, TextRole.Footnote);

        Gw.TooltipFor(
            ImGui.IsItemHovered(),
            rarestLine.Length > 0 || rarestFull.Length == 0
                ? Loc.F("header.collected.tooltip", countText, percentText)
                : Loc.F("header.collected.rarest.tooltip", countText, percentText, rarestFull));

        // ...and the two statistics lines sit beside it.
        ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y));
        Gw.Text(countText, TextRole.Subheadline);

        ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y + firstLine + stackGap));
        Gw.Text(remainingText, TextRole.Footnote);

        // The per-expansion strip draws on that second line while it clears the actions, and on a line of
        // its own under the counters when it does not. Dropping it in that band made a shipped feature look
        // removed at both narrow window sizes - indistinguishable from having never had one.
        if (stripInline)
        {
            DrawExpansionStrip(state, stripLeft, at.Y + firstLine + stackGap);
        }

        if (rarestLine.Length > 0)
        {
            // Centred against the count's line rather than sat on its baseline: a footnote beside a
            // subheadline that shared a baseline would read as a descender of it.
            ImGui.SetCursorScreenPos(new Vector2(rarestLeft, at.Y + ((firstLine - secondLine) * 0.5f)));
            Gw.Text(rarestLine, TextRole.Footnote);
        }

        var badgeText = RefreshWantedCount(state.WantedIds.Count);

        // The price watch's count shares that line. It is a count and not an action, so it needs no door of
        // its own; a line per feature would cost the grid 24 px for something most players never switch on,
        // and it is drawn at all only when something is watched ("Watching 0" is not a state to show).
        var watchingBadge = RefreshWatchingCount(state.WatchingCount);

        // The badges sit below the two statistics lines, so they are part of the block height. Without
        // one the badge overlapped the filter footnote (the wanted golden showed the collision).
        var badgeHeight = badgeText.Length > 0 || watchingBadge.Length > 0
            ? secondLine + stackGap
            : 0f;

        if (badgeHeight > 0f)
        {
            ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y + firstLine + secondLine + (stackGap * 2f)));
        }

        if (badgeText.Length > 0)
        {
            Gw.Badge(badgeText, look.Accent);

            // The badge is a shortcut into the list it counts: hovering it promises a click, and a click
            // shows the wanted mounts and opens the board that tracks them. The board is what a token
            // grind needs - the shelf answers "which", the board answers "how far".
            var badgeHovered = ImGui.IsItemHovered();

            Gw.TooltipFor(badgeHovered, Loc.T("header.wanted.hint"));

            if (badgeHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    state.Ownership = MountOwnership.Wanted;
                    state.BoardRequested = true;
                }
            }
        }

        if (watchingBadge.Length > 0)
        {
            if (badgeText.Length > 0)
            {
                ImGui.SameLine(0f, look.SpaceSmall * scale);
            }

            Gw.Badge(watchingBadge, look.Info);
            Gw.TooltipFor(ImGui.IsItemHovered(), Loc.T("header.watching.hint"));
        }

        // The strip's own line, under everything the counters block already draws. It costs the header one
        // footnote line and a gap, which is why the sizes that fit it inline are left exactly as they were.
        var stripBelow = stripWidth > 0f && !stripInline;

        if (stripBelow)
        {
            DrawExpansionStrip(state, textLeft, at.Y + firstLine + secondLine + (stackGap * 2f) + badgeHeight);
        }

        // The actions need to clear both lines: a button is taller than a text line, so sharing a
        // line with the statistics made them overlap ("Settings" over "2 still missing").
        if (actionsClaim > 0f)
        {
            var clearance = textLeft + MathF.Max(countWidth, remainingWidth) + (look.SpaceLarge * scale);

            if (actionsLeft > clearance)
            {
                DrawHeaderActions(state, actionsLeft, at.Y, look, scale);
            }
        }

        var stripHeight = stripBelow ? secondLine + stackGap : 0f;
        var blockHeight = MathF.Max(ringPx, firstLine + secondLine + stackGap + badgeHeight + stripHeight);

        ImGui.SetCursorScreenPos(new Vector2(at.X, at.Y + blockHeight + (look.SpaceSmall * scale)));

        // First run: one line naming the two controls a new player cannot find on their own - the search
        // box, which is the only way to narrow the whole book, and the Cards/Table switch, which changes how
        // the list draws rather than what is in it. Fitted to one line instead of wrapped because the shelf's
        // row budget is already spent above this point, and it is here for exactly one session.
        if (state.FirstRunHint)
        {
            Gw.Text(
                FitRole(Loc.T("firstrun.hint"), ImGui.GetContentRegionAvail().X, scale, TextRole.Footnote),
                TextRole.Footnote,
                look.TertiaryLabel);
        }

        // When a filter or a search narrows the list, say how much of the collection is on screen:
        // "136 of 369 collected" alone gives no feedback that the filter is doing anything.
        // Only when there is something on screen to describe: "0 of 0 shown collected (0%)" above an empty
        // list says nothing the empty state does not already say better.
        if (state.Items.Count > 0 && state.Items.Count != state.Total)
        {
            Gw.Text(RefreshFilterCounts(state), TextRole.Footnote);
        }

        Gw.Divider();
    }

    /// <summary>
    /// Per-expansion completion, drawn on the statistics line to the right of "2 still missing".
    /// The caller has already established that it fits.
    /// </summary>
    private void DrawExpansionStrip(MountViewState state, float left, float top)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;
        var spacing = look.SpaceLarge * scale;

        for (var index = 0; index < state.Expansions.Count; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine(0f, spacing);
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(left, top));
            }

            // The strip is the filter for the expansion it counts, not decoration: the label of the
            // active one takes the accent, a hover promises a click, and the click toggles it (clicking
            // the active one goes back to every expansion). It is drawn as text either way, so no
            // golden moves where the filter is All.
            var filter = MountBreakdown.FilterOf(state.Expansions[index]);
            var active = state.Expansion == filter;

            Gw.Text(ExpansionLabel(state, index), TextRole.Footnote, active ? look.Accent : null);

            // A hand-rolled hit test, because Gw.Text reserves its box with a Dummy that carries no ImGui
            // id: IsItemHovered() never reports it, which is why the tooltip this replaces never appeared.
            // Same rule as every other hand-rolled test here - the window must own the pointer as well as
            // the rect, or a dropdown or the settings window over the strip would click it.
            var overLabel = ImGui.IsWindowHovered()
                && ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());


            if (overLabel)
            {
                ExpansionProgress expansion = state.Expansions[index];
                Gw.TooltipFor(
                    true,
                    Loc.F(
                        "header.expansion.tooltip",
                        UiText.Expansion(expansion.Name),
                        expansion.Owned.ToString(CultureInfo.InvariantCulture),
                        expansion.Total.ToString(CultureInfo.InvariantCulture),
                        active ? Loc.T("header.expansion.widen") : Loc.T("header.expansion.narrow")));

                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    state.Expansion = active ? MountExpansion.All : filter;
                }
            }
        }
    }

    /// <summary>The width the whole strip needs, or zero when there is nothing to draw.</summary>
    private float StripWidth(MountViewState state, Look look, float scale)
    {
        if (state.Expansions.Count == 0)
        {
            return 0f;
        }

        var spacing = look.SpaceLarge * scale;
        var total = 0f;

        for (var index = 0; index < state.Expansions.Count; index++)
        {
            total += ExpansionWidth(state, index);

            if (index > 0)
            {
                total += spacing;
            }
        }

        return total;
    }

    /// <summary>The cached widths of the statistics strings, measured when the numbers change.</summary>
    private float countWidthCache;

    private float percentWidthCache;

    private float remainingWidthCache;

    /// <summary>The cached width of each expansion label, measured with the labels.</summary>
    private float[] expansionWidths = [];

    /// <summary>The cached `ARR 34%` label for one expansion.</summary>
    private string ExpansionLabel(MountViewState state, int index)
    {
        RefillExpansionLabels(state);
        return expansionLabels[index];
    }

    /// <summary>The cached width of one expansion label.</summary>
    private float ExpansionWidth(MountViewState state, int index)
    {
        RefillExpansionLabels(state);
        return expansionWidths[index];
    }

    private void RefillExpansionLabels(MountViewState state)
    {
        var changed = state.Expansions.Count != expansionLabels.Length;

        if (!changed)
        {
            for (var index = 0; index < expansionLabels.Length; index++)
            {
                if (expansionCounts[index, 0] != state.Expansions[index].Owned
                    || expansionCounts[index, 1] != state.Expansions[index].Total)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        expansionLabels = new string[state.Expansions.Count];
        expansionWidths = new float[state.Expansions.Count];
        expansionCounts = new int[state.Expansions.Count, 2];

        for (var index = 0; index < expansionLabels.Length; index++)
        {
            ExpansionProgress expansion = state.Expansions[index];
            expansionLabels[index] = Loc.F(
                "header.expansion.label",
                UiText.ExpansionShort(expansion.ShortName),
                expansion.Percent.ToString(CultureInfo.InvariantCulture));
            var look = Frame.Look;
            expansionWidths[index] = TextPaint.Measure(expansionLabels[index], TextRole.Footnote, in look, Frame.Scale).X;
            expansionCounts[index, 0] = expansion.Owned;
            expansionCounts[index, 1] = expansion.Total;
        }
    }

    private void RefreshCounts(MountViewState state)
    {
        if (state.Owned == countedOwned && state.Total == countedTotal)
        {
            return;
        }

        countedOwned = state.Owned;
        countedTotal = state.Total;
        countText = Loc.F("header.collected", FormatInt(state.Owned), FormatInt(state.Total));
        percentText = Loc.F("header.percent", FormatInt(MountProgress.Percent(state.Owned, state.Total)));

        var look = Frame.Look;
        var scale = Frame.Scale;
        countWidthCache = TextPaint.Measure(countText, TextRole.Subheadline, in look, scale).X;
        percentWidthCache = TextPaint.Measure(percentText, TextRole.Footnote, in look, scale).X;
        remainingText = state.Total switch
        {
            0 => Loc.T("header.remaining.none"),
            _ when state.Total > state.Owned => Loc.F("header.remaining.some", FormatInt(state.Total - state.Owned)),
            _ => Loc.T("header.remaining.all"),
        };

        remainingWidthCache = TextPaint.Measure(remainingText, TextRole.Footnote, in look, scale).X;
    }

    private string RefreshWantedCount(int wanted)
    {
        if (wanted == countedWanted)
        {
            return wantedText;
        }

        countedWanted = wanted;
        wantedText = wanted <= 0 ? string.Empty : Loc.F("header.wanted", FormatInt(wanted));
        return wantedText;
    }

    /// <summary>The rarest outstanding mount as one sentence, or empty when there is nothing to name.</summary>
    private string rarestFull = string.Empty;

    /// <summary>The same sentence without its leading word, for a line with less room.</summary>
    private string rarestCompact = string.Empty;

    private float rarestFullWidth;

    private float rarestCompactWidth;

    private RarestMissing? countedRarest;

    private float rarestMeasuredScale = -1f;

    /// <summary>
    /// Builds and measures the insight sentence for <paramref name="rarest"/>, once per mount and UI scale:
    /// this runs inside the per-frame draw, and the sentence is two string formats plus two text measurements.
    /// </summary>
    /// <param name="rarest">The mount to name, or null.</param>
    private void RefillRarest(RarestMissing? rarest)
    {
        var scale = Frame.Scale;

        if (countedRarest == rarest && rarestMeasuredScale == scale)
        {
            return;
        }

        countedRarest = rarest;
        rarestMeasuredScale = scale;

        var look = Frame.Look;

        rarestFull = MountRarityInsight.Line(rarest);
        rarestCompact = MountRarityInsight.CompactLine(rarest);
        rarestFullWidth = rarestFull.Length == 0
            ? 0f
            : TextPaint.Measure(rarestFull, TextRole.Footnote, in look, scale).X;
        rarestCompactWidth = rarestCompact.Length == 0
            ? 0f
            : TextPaint.Measure(rarestCompact, TextRole.Footnote, in look, scale).X;
    }

    /// <summary>
    /// The longest form of the sentence that fits <paramref name="room"/>, or empty when neither does.
    /// </summary>
    /// <remarks>
    /// Two forms rather than an ellipsis: an ellipsised mount name is ambiguous, and the reader of a header
    /// knows the word "rarest" from the line itself. The measure is the same one the draw uses, so "fits"
    /// means what it says.
    /// </remarks>
    /// <param name="room">The width left on the counters' line, in pixels.</param>
    private string RarestLine(float room)
    {
        if (rarestFullWidth > 0f && rarestFullWidth <= room)
        {
            return rarestFull;
        }

        return rarestCompactWidth > 0f && rarestCompactWidth <= room ? rarestCompact : string.Empty;
    }

    /// <summary>
    /// The watch badge, built once per count: <see cref="MountWatch.HeaderLabel"/> formats through the string
    /// table, and this runs inside the per-frame draw.
    /// </summary>
    /// <param name="watching">How many mounts are watched.</param>
    private string RefreshWatchingCount(int watching)
    {
        if (watching == countedWatching)
        {
            return watchingText;
        }

        countedWatching = watching;
        watchingText = MountWatch.HeaderLabel(watching);

        return watchingText;
    }

    /// <summary>
    /// The four chip labels with their counts, e.g. `All 12`. Rebuilt only when a count changes: the
    /// draw path runs every frame and must not allocate four strings per frame.
    /// </summary>
    private string[] RefreshOwnershipLabels(MountViewState state)
    {
        if (state.OwnershipCounts.Count != OwnershipNames.Length)
        {
            return ownershipLabels;
        }

        var changed = false;

        for (var index = 0; index < OwnershipNames.Length; index++)
        {
            if (ownershipCounts[index] != state.OwnershipCounts[index])
            {
                changed = true;
                break;
            }
        }

        if (!changed && ownershipCounts[0] >= 0)
        {
            return ownershipLabels;
        }

        for (var index = 0; index < OwnershipNames.Length; index++)
        {
            ownershipCounts[index] = state.OwnershipCounts[index];
            ownershipLabels[index] = Loc.F(
                "chip.count",
                OwnershipNames[index],
                ownershipCounts[index].ToString(CultureInfo.InvariantCulture));
        }

        return ownershipLabels;
    }
}
