using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Glasswork.Effects;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>Part of <see cref="MountbookView"/>: grid drawing.</summary>
public sealed partial class MountbookView
{

    /// <summary>Eased hover lift, 0 to 1; one value is enough because one card hovers at a time.</summary>
    private float cardLift;

    /// <summary>The mount the current lift belongs to, so the glow never follows the pointer around.</summary>
    /// <summary>The card the pointer was over on the last draw, which the hover ease follows.</summary>
    private uint hoveredCardId;
    private void DrawFilterColumn(MountViewState state, float width, float height)
    {
        if (!ImGui.BeginChild("##filters"u8, new Vector2(width, height)))
        {
            ImGui.EndChild();
            return;
        }

        DrawFilters(state);

        var gridHeight = MathF.Max(ImGui.GetContentRegionAvail().Y, 1f);

        // The no-results state replaces the scrolling child instead of living inside it: in a short
        // window the child is only ~100 px tall, so a state block drawn in it was clipped and faded.
        if (state.Items.Count == 0)
        {
            DrawEmptyState(state, gridHeight);
            ImGui.EndChild();
            return;
        }

        PushScrollbarInk();

        if (ImGui.BeginChild("##mounts"u8, new Vector2(0f, gridHeight)))
        {
            if (state.ViewMode == MountViewMode.Table)
            {
                DrawTable(state);
            }
            else
            {
                DrawGrid(state);
            }

            Gw.EdgeFade();
        }

        ImGui.EndChild();

        PopScrollbarInk();

        ImGui.EndChild();
    }

    /// <summary>
    /// The search box and the three filters. Every control writes straight back into the state; the
    /// window re-queries from those fields on its next frame, so the list lags a keystroke by one frame.
    /// </summary>
    private void DrawFilters(MountViewState state)
    {
        // The controls fill the column, and a hovered control grows past it (see PushControlRoom). The
        // push is paired in a finally: MainWindow keeps drawing after a failed frame, so a throw that
        // skipped the pop would leave the clip stack one short for the rest of the session (and pop a
        // clip the caller never pushed).
        PushControlRoom();

        try
        {
            var search = state.Search;
            _ = Gw.TextField(ref search, hint: "Search mounts and sources"u8);
            state.Search = search;

            // The operators exist but nothing on screen advertises them; a tooltip is the one hint that
            // costs no layout in any of the goldens.
            Gw.TooltipFor(
                ImGui.IsItemHovered(),
                "Type to search. Narrow with tradeable:y, music:y, barding:y, flying:y, seats:>=2, patch:>=6.1 or owned:<1.");

            var ownership = (int)state.Ownership;

            // Four chips do not fit the narrow window (the last label clipped to "Unown?"), so the control
            // changes shape rather than truncating: chips when there is room, a combo when not.
            if (ImGui.GetContentRegionAvail().X >= (380f * Frame.Scale))
            {
                _ = Gw.Segmented(ref ownership, RefreshOwnershipLabels(state));
            }
            else
            {
                ImGui.SetNextItemWidth(-1f);
                _ = Gw.ComboBox(ref ownership, RefreshOwnershipLabels(state), label: "show"u8);
            }

            state.Ownership = (MountOwnership)ownership;

            var expansion = (int)state.Expansion;
            _ = Gw.ComboBox(ref expansion, ExpansionLabels, label: "Expansion"u8);
            state.Expansion = (MountExpansion)expansion;

            // The acquire-method filter: "what do I still need from PvP" without a search that also matches lore.
            var source = SourceIndex(state);
            _ = Gw.ComboBox(ref source, state.SourceLabels, label: "Source"u8);
            state.SourceFilter = source <= 0 || source >= state.SourceLabels.Length
                ? string.Empty
                : state.SourceLabels[source];

            var order = (int)state.Order;
            _ = Gw.ComboBox(ref order, OrderLabels, label: "Order"u8);
            state.Order = (MountOrder)order;

            // Grouping is a card-grid feature: the table already has a column for every fact the headings
            // would repeat, so the control only appears where it does something.
            if (state.ViewMode == MountViewMode.Grid)
            {
                var grouping = (int)state.GroupBy;
                _ = Gw.ComboBox(ref grouping, GroupLabels, label: "Group by"u8);
                state.GroupBy = (MountGrouping)grouping;
            }

            // Cards or the dense table. It sits with the filters because it changes how the list is
            // drawn, not what is in it, and because a fifth header button cost the per-expansion strip
            // its room in a 1040 px window - the strip is a feature, the switch is a preference.
            var view = (int)state.ViewMode;
            if (Gw.Segmented(ref view, ViewLabels))
            {
                state.ViewMode = (MountViewMode)view;
            }
        }
        finally
        {
            PopControlRoom();
        }
    }

    /// <summary>The combo index of the active source filter: 0 ("Any") when it is empty or unknown.</summary>
    private static int SourceIndex(MountViewState state)
    {
        if (string.IsNullOrEmpty(state.SourceFilter))
        {
            return 0;
        }

        for (var index = 1; index < state.SourceLabels.Length; index++)
        {
            if (string.Equals(state.SourceLabels[index], state.SourceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// The no-results state: a heading, one line that says what to do, and a button that does it.
    /// Placed about a third of the way down the card rather than at the very top, so it reads as a
    /// state of the list instead of a stray label.
    /// </summary>
    private static void DrawEmptyState(MountViewState state, float height)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;
        var blockHeight = (look.Type.LineHeight(TextRole.Title) * scale)
            + (look.Type.LineHeight(TextRole.Footnote) * scale)
            + look.ControlHeight
            + (look.SpaceMedium * scale * 2f);
        var top = ImGui.GetCursorScreenPos().Y + MathF.Max((height - blockHeight) * 0.5f, 0f);
        var left = ImGui.GetCursorScreenPos().X;

        // An empty *wanted* list is not a filter problem: it is a list with nothing on it yet, and the hint
        // should say how to add something rather than how to widen a filter.
        var onWantedList = state.Ownership == MountOwnership.Wanted;

        ImGui.SetCursorScreenPos(new Vector2(left, top));
        Gw.Heading(onWantedList ? "Nothing on your wanted list" : "No mounts match");

        Gw.Text(
            onWantedList
                ? "Click the dot on a card or the Want button to add one."
                : "Clear the search, or widen the filters.",
            TextRole.Footnote);

        ImGui.Dummy(new Vector2(0f, look.SpaceMedium * scale));

        if (Gw.Button(onWantedList ? "Show all mounts"u8 : "Clear filters"u8, ButtonStyle.Default, ButtonSize.Small))
        {
            state.ClearFiltersRequested = true;
        }
    }

    /// <summary>
    /// Lays the cards out on an explicit grid: position is computed from the row and column, so a long
    /// name can never shift the column after it. <see cref="Gw.EdgeFade"/> in the caller fades the
    /// scrolling edges instead of clipping them hard.
    /// </summary>
    private void DrawGrid(MountViewState state)
    {
        // A justified grid: the column count comes from the width, the leftover space is shared
        // between the gutters instead of being dumped at the right edge, and every row uses one
        // pitch. That is what makes the tiles read as a lattice rather than as a list with holes.
        var scale = Frame.Scale;
        var iconPx = IconSize * scale;
        var cellPx = CellWidth * scale;
        var lineHeight = Frame.Look.Type.LineHeight(TextRole.Callout) * scale;
        var minGutter = Frame.Look.SpaceLarge * scale;
        var captionGap = Frame.Look.SpaceSmall * scale;
        var rowGap = Frame.Look.SpaceLarge * scale;

        // Inset the whole grid: flush against the scrollbar and the panel edge reads as packed even
        // when the gutters themselves are even.
        // SpaceSmall is the visual margin; the extra 6 px is room for the hover and selection halos, which
        // paint outside the card and were being clipped by the child's clip rectangle on the outer columns.
        var inset = (Frame.Look.SpaceSmall + 6f) * scale;
        // The scrollbar is drawn inside the child's content region, so the right inset is only real
        // if its width is reserved as well - otherwise the last column hugs the bar.
        var scrollbar = ImGui.GetScrollMaxY() > 0f ? ImGui.GetStyle().ScrollbarSize : 0f;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(inset, 0f);
        var available = MathF.Max(ImGui.GetContentRegionAvail().X - (inset * 2f) - scrollbar, 1f);
        var perRow = Math.Max(1, (int)((available + minGutter) / (cellPx + minGutter)));
        var gutter = perRow > 1 ? MathF.Max(minGutter, (available - (perRow * cellPx)) / (perRow - 1)) : 0f;

        var pitchX = cellPx + gutter;
        var pitchY = iconPx + captionGap + (lineHeight * 2f) + rowGap;

        // The hover ease advances once per frame, from the card the pointer was over last frame. It used
        // to advance inside the card body, which multiplied it by (1 - easing) for every card drawn
        // before the hovered one: with sixty cards the glow was already under the visible threshold by
        // the middle of the grid, so only the last row ever lit up.
        cardLift += ((hoveredCardId != 0 ? 1f : 0f) - cardLift) * MathF.Min(1f, ImGui.GetIO().DeltaTime * 14f);
        hoveredCardId = 0;

        // The sections share the same lattice as the flat grid, one run of rows each: grouping changes
        // where a row starts, never the column pitch, so the tiles stay in one lattice either way.
        IReadOnlyList<MountGroup> groups = MountGroups.Build(state.Items, state.GroupBy);
        var headerPitch = (Frame.Look.Type.LineHeight(TextRole.Caption) + Frame.Look.SpaceMedium) * scale;
        var y = origin.Y;

        foreach (MountGroup group in groups)
        {
            if (group.Title.Length > 0)
            {
                ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
                Gw.Text(group.Title, TextRole.Caption, Frame.Look.SecondaryLabel);
                ImGui.SameLine(0f, Frame.Look.SpaceSmall * scale);
                Gw.Text(FormatInt(group.Items.Count), TextRole.Caption, Frame.Look.TertiaryLabel);

                y += headerPitch;
            }

            for (var index = 0; index < group.Items.Count; index++)
            {
                var cell = new Vector2(
                    origin.X + ((index % perRow) * pitchX),
                    y + ((index / perRow) * pitchY));

                DrawCard(state, group.Items[index], cell, cellPx, iconPx, lineHeight, captionGap);
            }

            int rows = (group.Items.Count + perRow - 1) / perRow;
            y += rows * pitchY;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    private void DrawCard(
        MountViewState state,
        MountShelfItem item,
        Vector2 cell,
        float cellPx,
        float iconPx,
        float lineHeight,
        float captionGap)
    {
        var scale = Frame.Scale;
        var selected = state.SelectedId == item.Id;
        var tileAt = new Vector2(cell.X + ((cellPx - iconPx) * 0.5f), cell.Y);

        // The pointer is measured before the tile so the glow can react in the same frame. Glasswork's
        // own hover spring is `internal`, so this is a two-line exponential ease on the frame delta:
        // no allocation, no wall clock, and it settles in about a tenth of a second.
        var cellMax = new Vector2(cell.X + cellPx, cell.Y + iconPx + captionGap + (lineHeight * 2f));

        // The rect test alone only intersects the clip rect, so a click on a filter dropdown or the
        // settings window that happens to sit over a card was also seen here - and a double-click
        // there summoned a mount nobody pointed at. This window must own the pointer as well.
        var hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(cell, cellMax, true);

        if (hovered)
        {
            hoveredCardId = item.Id;
        }

        var lift = hovered ? cardLift : 0f;

        ImGui.SetCursorScreenPos(tileAt);

        if (!item.Owned)
        {
            ImGui.BeginDisabled();
        }

        if (selected)
        {
            Gw.BeginGlow(new GlowSpec
            {
                Intensity = 0.55f,
                Radius = 4f,
            });
        }
        else if (lift > 0.01f)
        {
            // The hover lift lukat liked. Its halo paints outside the card, so the grid reserves room for
            // it on both sides (see the inset below) - without that the outer columns were cut off.
            Gw.BeginGlow(new GlowSpec
            {
                Intensity = 0.22f * lift,
                Radius = 3f,
            });
        }

        // Press feedback: while the button is held over the card the tile is drawn a fraction smaller, so a
        // press reads before anything happens. It cannot disturb the layout - the cell keeps its pitch and the
        // captions are placed from the cell, not from the tile - and nothing is drawn when not pressed, which
        // keeps the goldens byte-identical.
        var pressed = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var pressInset = pressed ? 2f * Frame.Scale : 0f;

        ImGui.SetCursorScreenPos(tileAt + new Vector2(pressInset, pressInset));
        Gw.Image(
            TextureFor(state, item.IconId),
            new Vector2(IconSize - (pressInset * 2f), IconSize - (pressInset * 2f)),
            radius: Frame.Look.RadiusSmall);

        if (selected || lift > 0.01f)
        {
            Gw.EndGlow();
        }

        if (selected)
        {
            // A crisp accent ring in addition to the soft glow: at a glance the glow alone made the
            // selected card look merely "brighter" than its neighbours.
            var radius = Frame.Look.RadiusSmall * Frame.Scale;

            // Inset by half the stroke: a 1.5 px ring drawn exactly on the tile edge loses half of itself
            // to the child's clip rectangle on the outermost columns.
            var ringInset = 0.75f * Frame.Scale;
            ImGui.GetWindowDrawList().AddRect(
                tileAt + new Vector2(ringInset, ringInset),
                tileAt + new Vector2(iconPx - ringInset, iconPx - ringInset),
                ImGui.GetColorU32(Frame.Look.Accent.ToVector4()),
                radius,
                1.5f * Frame.Scale);
        }

        if (!item.Owned)
        {
            ImGui.EndDisabled();
        }

        // The wanted marker sits on the tile's top-right corner, outside the unowned disabled scope:
        // marking an uncollected mount as hunted is the point of the feature.
        //
        // A drawn dot, not a FontAwesome star: U+F005 is missing from some font atlases and a missing
        // glyph drew a "?" on every card (it did, in the golden). A circle from the draw list needs no
        // font coverage, uses Look tokens, and renders identically in game and in the preview.
        var wanted = state.WantedIds.Contains(item.Id);
        var markerRadius = 5f * Frame.Scale;
        var markerCentre = new Vector2(
            tileAt.X + iconPx - markerRadius - (4f * Frame.Scale),
            tileAt.Y + markerRadius + (4f * Frame.Scale));

        var markerList = ImGui.GetWindowDrawList();

        // A square in the opposite corner for "collected while you were playing": a different shape, so
        // it cannot be confused with the round wanted marker on the same tile.
        if (state.RecentIds.Contains(item.Id))
        {
            var freshSide = markerRadius * 1.6f;
            var freshMin = new Vector2(
                tileAt.X + iconPx - freshSide - (4f * Frame.Scale),
                tileAt.Y + iconPx - freshSide - (4f * Frame.Scale));

            markerList.AddRectFilled(
                freshMin,
                freshMin + new Vector2(freshSide, freshSide),
                ImGui.GetColorU32(Frame.Look.Accent.ToVector4()),
                2f * Frame.Scale);

            var freshHovered = ImGui.IsMouseHoveringRect(freshMin, freshMin + new Vector2(freshSide, freshSide), true);
            Gw.TooltipFor(freshHovered, "Collected recently");
        }

        if (wanted)
        {
            markerList.AddCircleFilled(markerCentre, markerRadius, ImGui.GetColorU32(Frame.Look.Accent.ToVector4()));
        }
        else
        {
            markerList.AddCircle(
                markerCentre,
                markerRadius,
                ImGui.GetColorU32(Frame.Look.SecondaryLabel.ToVector4()),
                12,
                1.5f * Frame.Scale);
        }

        // A generous square hit target: the drawn dot is small, and with no keyboard path the mouse is
        // the only way to build the list, so a near-miss must still land.
        var markerReach = markerRadius * 3.6f;
        var markerHovered = ImGui.IsMouseHoveringRect(
            markerCentre - new Vector2(markerReach, markerReach),
            markerCentre + new Vector2(markerReach, markerReach),
            true);

        if (markerHovered)
        {
            markerList.AddCircle(
                markerCentre,
                markerReach * 0.72f,
                ImGui.GetColorU32(Frame.Look.Accent.ToVector4()),
                20,
                1f * Frame.Scale);
        }

        var wantedToggled = markerHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        Gw.TooltipFor(markerHovered, wanted ? "Remove from the wanted list" : "Add to the wanted list");

        if (wantedToggled)
        {
            state.ToggleWantedId = item.Id;
        }

        // The caption is centred under its tile, not pinned to the cell's left edge: with a centred
        // tile a left-aligned label reads as a mistake even when the pitch is exact.
        Caption caption = CaptionFor(item, cellPx - (Frame.Look.SpaceExtraSmall * 2f * scale), scale);
        var captionTop = cell.Y + iconPx + captionGap;
        var ink = selected ? Frame.Look.Accent : Frame.Look.Label;

        // Two lines, each measured and centred, instead of one ellipsised line: "magitek death claw"
        // fits as "magitek death" / "claw", which one line could never show. Drawn as explicit labels
        // because Gw.TextWrapped ignores the pushed wrap position.
        DrawCaptionLine(caption.First, caption.FirstWidth, cell.X, cellPx, captionTop, ink);

        if (caption.Second.Length > 0)
        {
            DrawCaptionLine(caption.Second, caption.SecondWidth, cell.X, cellPx, captionTop + lineHeight, ink);
        }


        // The tooltip says what a double-click does, which is the one card gesture with no visible
        // affordance. Built only while hovered, so it costs nothing per frame.
        // "\n", not Environment.NewLine: the view must not format for the machine it happens to run on
        // (T7), and the tooltip renderer breaks on \n either way.
        if (hovered && !markerHovered)
        {
            Gw.TooltipFor(
                true,
                item.Owned
                    ? string.Concat(item.Name, "\n", "Double-click to ride")
                    : string.Concat(item.Name, "\n", "Not collected"));
        }

        if (!markerHovered && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            state.SelectedId = item.Id;
        }

        // Double-click is the quick action a shelf expects: select and summon. The window still
        // decides whether the mount can actually be ridden; the view never acts on its own.
        if (!markerHovered && hovered && item.Owned && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.SelectedId = item.Id;
            state.RideRequested = true;
        }
    }

    /// <summary>
    /// The caption to draw for a card: word-wrapped to at most two lines, each measured with the
    /// renderer's own text paint, and shortened with an ellipsis only when two lines still overflow.
    /// </summary>
    private static Caption CaptionFor(MountShelfItem item, float width, float scale)
    {
        var key = (item.Id, (int)width);
        if (Captions.TryGetValue(key, out Caption cached))
        {
            return cached;
        }

        var caption = Wrap(item.Name, width, scale);
        if (Captions.Count > 1024)
        {
            Captions.Clear();
        }

        Captions[key] = caption;
        return caption;
    }

    private static Caption Wrap(string name, float width, float scale)
    {
        var whole = CaptionWidth(name, scale);
        if (whole <= width)
        {
            return new Caption(name, string.Empty, whole, 0f);
        }

        var first = string.Empty;
        var second = string.Empty;

        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var firstCandidate = first.Length == 0 ? word : first + " " + word;
            if (CaptionWidth(firstCandidate, scale) <= width)
            {
                first = firstCandidate;
                continue;
            }

            var secondCandidate = second.Length == 0 ? word : second + " " + word;
            if (CaptionWidth(secondCandidate, scale) <= width)
            {
                second = secondCandidate;
                continue;
            }

            // Both lines are full: the rest is dropped behind an ellipsis on the second line.
            second = Fit(secondCandidate, width, scale);
            break;
        }

        if (second.Length > 0)
        {
            second = Fit(second, width, scale);
        }

        return new Caption(first, second, CaptionWidth(first, scale), CaptionWidth(second, scale));
    }

    /// <summary>
    /// The width the caption really takes. Measured with the renderer's own text paint, not with
    /// ImGui.CalcTextSize: Gw.Text draws in Glasswork's font at the Look's type scale, so ImGui's
    /// measurement came out narrow and names were cut just before the ellipsis.
    /// </summary>
    private static float CaptionWidth(string text, float scale)
    {
        var look = Frame.Look;
        return TextPaint.Measure(text, TextRole.Callout, in look, scale).X;
    }

    private static void DrawCaptionLine(string text, float width, float cellX, float cellWidth, float y, Color ink)
    {
        if (text.Length == 0)
        {
            return;
        }

        var x = cellX + MathF.Max((cellWidth - width) * 0.5f, 0f);
        var lineHeight = Frame.Look.Type.LineHeight(TextRole.Callout) * Frame.Scale;

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.PushClipRect(new Vector2(cellX, y), new Vector2(cellX + cellWidth, y + lineHeight), true);
        Gw.Text(text, TextRole.Callout, ink);
        ImGui.PopClipRect();
    }
}
