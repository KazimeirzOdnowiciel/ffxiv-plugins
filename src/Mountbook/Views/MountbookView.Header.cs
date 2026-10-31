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
    /// Settings / Refresh / Themes / Copy missing, right-aligned in the header. Small and plain (text
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
            Span<string> labels = ["Settings", "Refresh", "Themes", "Copy missing"];
            Span<string> hints =
            [
                "Open the settings window",
            "Re-read your collection from the game",
            "Open the folder the theme JSONs live in",
            "Copy every uncollected mount in this list to the clipboard",
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
                        case 2:
                            state.ThemesRequested = true;
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

    /// <summary>The four header actions' label widths, measured once per UI scale.</summary>
    private static float[] ActionWidths(Look look, float scale)
    {
        if (actionWidthsScale == scale && actionWidths.Length == 4)
        {
            return actionWidths;
        }

        Span<string> labels = ["Settings", "Refresh", "Themes", "Copy missing"];
        actionWidths = new float[4];
        actionWidthsScale = scale;

        for (var index = 0; index < labels.Length; index++)
        {
            // What Gw.Button(ButtonSize.Small) reserves: the footnote role at semibold, one medium space
            // each side, and never narrower than the small control height. This used to measure the callout
            // role, which is larger, so every button was followed by a few pixels more than the gap.
            actionWidths[index] = MathF.Max(
                TextPaint.Measure(labels[index], ActionLabelStyle, in look, scale).X
                    + (look.SpaceMedium * scale * 2f),
                look.ControlHeightSmall * scale);
        }

        return actionWidths;
    }

    /// <summary>The width the four header actions need, or zero when there are none to draw.</summary>
    private static float ActionsWidth(Look look, float scale)
    {
        var widths = ActionWidths(look, scale);
        var total = 3f * look.SpaceSmall * scale;

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

        Gw.Heading("Mountbook"u8);

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
            string.Concat(countText, " - ", percentText, " of the game's mounts"));

        // ...and the two statistics lines sit beside it.
        ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y));
        Gw.Text(countText, TextRole.Subheadline);

        ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y + firstLine + stackGap));
        Gw.Text(remainingText, TextRole.Footnote);

        // The per-expansion strip only draws on that second line if it fits before the actions'
        // clearance; otherwise it is dropped whole rather than truncated.
        var stripLeft = textLeft + remainingWidth + (look.SpaceLarge * scale);
        var actionsClaim = ActionsWidth(look, scale);
        var actionsLeft = contentRight - actionsClaim;
        var stripRight = stripLeft + StripWidth(state, look, scale);

        if (actionsClaim > 0f && stripRight < actionsLeft - (look.SpaceSmall * scale))
        {
            DrawExpansionStrip(state, stripLeft, at.Y + firstLine + stackGap);
        }

        var badgeText = RefreshWantedCount(state.WantedIds.Count);

        if (badgeText.Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(textLeft, at.Y + firstLine + secondLine + (stackGap * 2f)));
            Gw.Badge(badgeText, look.Accent);

            // The badge is a shortcut into the list it counts: hovering it promises a click, and a
            // click switches the ownership filter to Wanted.
            var badgeHovered = ImGui.IsItemHovered();

            Gw.TooltipFor(badgeHovered, "Click to show only your wanted mounts");

            if (badgeHovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    state.Ownership = MountOwnership.Wanted;
                }
            }
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

        // The badge sits below the two statistics lines, so it is part of the block height. Without
        // it the badge overlapped the filter footnote (the wanted golden showed the collision).
        var badgeHeight = badgeText.Length > 0
            ? (look.Type.LineHeight(TextRole.Footnote) * scale) + stackGap
            : 0f;
        var blockHeight = MathF.Max(ringPx, firstLine + secondLine + stackGap + badgeHeight);

        ImGui.SetCursorScreenPos(new Vector2(at.X, at.Y + blockHeight + (look.SpaceSmall * scale)));

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

            Gw.Text(ExpansionLabel(state, index), TextRole.Footnote);

            if (ImGui.IsItemHovered())
            {
                ExpansionProgress expansion = state.Expansions[index];
                Gw.TooltipFor(
                    true,
                    string.Concat(
                        expansion.Name,
                        ": ",
                        expansion.Owned.ToString(CultureInfo.InvariantCulture),
                        " of ",
                        expansion.Total.ToString(CultureInfo.InvariantCulture),
                        " collected"));
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
            expansionLabels[index] = string.Concat(
                expansion.ShortName,
                " ",
                expansion.Percent.ToString(CultureInfo.InvariantCulture),
                "%");
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
        countText = string.Concat(
            FormatInt(state.Owned),
            " of ",
            FormatInt(state.Total),
            " collected");
        percentText = string.Concat(FormatInt(MountProgress.Percent(state.Owned, state.Total)), "%");

        var look = Frame.Look;
        var scale = Frame.Scale;
        countWidthCache = TextPaint.Measure(countText, TextRole.Subheadline, in look, scale).X;
        percentWidthCache = TextPaint.Measure(percentText, TextRole.Footnote, in look, scale).X;
        remainingText = state.Total switch
        {
            0 => "no mounts loaded",
            _ when state.Total > state.Owned => string.Concat(
                FormatInt(state.Total - state.Owned),
                " still missing"),
            _ => "every mount collected",
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
        wantedText = wanted <= 0 ? string.Empty : string.Concat(FormatInt(wanted), " wanted");
        return wantedText;
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
            ownershipLabels[index] = string.Concat(
                OwnershipNames[index],
                " ",
                ownershipCounts[index].ToString(CultureInfo.InvariantCulture));
        }

        return ownershipLabels;
    }
}
