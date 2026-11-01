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

    /// <summary>The card the pointer was over on the last draw, which the hover ease follows.</summary>
    private uint hoveredCardId;

    /// <summary>The filter block's own height, measured on an earlier frame; not positive until the first measurement lands.</summary>
    private float filtersHeightPx = -1f;

    /// <summary>The grouped shelf last built, and the two values it was built from: the list by reference (the
    /// window hands over a new one only when the query re-ran) and the grouping it was split by.</summary>
    private static IReadOnlyList<MountGroup>? cachedGroups;
    private static IReadOnlyList<MountShelfItem>? cachedGroupsSource;
    private static MountGrouping cachedGroupsBy;

    /// <summary>The UI scale <see cref="filtersHeightPx"/> was measured at: the height is re-taken every frame,
    /// so this only says whether the previous one is still usable for the squeeze test.</summary>
    private float filtersHeightScale = -1f;

    private void DrawFilterColumn(MountViewState state, float width, float height)
    {
        if (!ImGui.BeginChild("##filters"u8, new Vector2(width, height)))
        {
            ImGui.EndChild();
            return;
        }

        var filtersTop = ImGui.GetCursorScreenPos().Y;

        // The one door into the saved views that exists before there are any: the row and its button are drawn
        // only once a preset exists, so a row-only affordance would leave nothing to save the first one with. A
        // right-click on the filter block opens the dialog and costs no pixels anywhere - a closed popup draws
        // nothing - which is what lets "draw nothing with no presets" and "there is always a way to save one"
        // both hold. The search field's tooltip names it, so it is discoverable without a control.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            state.ViewsDialogOpen = true;
        }

        // At the window's 820x520 minimum the filter block alone is taller than the column, so the list is
        // left no height at all - and the empty state is drawn into the list, which put "no mounts match" and
        // its one action below the fold of a scrolling child: measured, the room left was 1 px. The filters'
        // height is only known after they are drawn, so it is remembered from the previous frame (the same
        // shape as the card's bottom inset) and, while the room left is under the floor of the state, the
        // state is drawn first as one row above the search box. The condition reads the filters, never the
        // state, so it cannot flip between frames.
        var squeezed = state.Items.Count == 0
            && filtersHeightScale == Frame.Scale
            && (height - filtersHeightPx) < EmptyStateFloorHeight();

        if (squeezed)
        {
            DrawCompactEmptyState(state);
        }

        DrawFilters(state);

        filtersHeightPx = ImGui.GetCursorScreenPos().Y - filtersTop;
        filtersHeightScale = Frame.Scale;

        var gridHeight = MathF.Max(ImGui.GetContentRegionAvail().Y, 1f);

        // The no-results state replaces the scrolling child instead of living inside it: in a short
        // window the child is only ~100 px tall, so a state block drawn in it was clipped and faded.
        if (state.Items.Count == 0)
        {
            if (!squeezed)
            {
                DrawEmptyState(state, gridHeight);
            }

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
    /// The search box, the Cards/Table switch, the ownership chips and the four combos, on four rows.
    /// Every control writes straight back into the state; the window re-queries from those fields on its next
    /// frame, so the list lags a keystroke by one frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The block is folded into pairs because it was eating the list: seven rows of one control each took 321 of
    /// the 505 px the list column has at the default window size, and at the window's 820x520 minimum the whole
    /// column - the grid then drew no card at all, which the `minimum` golden showed as an empty pane. Two
    /// controls share a row here: the search field and the Cards/Table switch, then the ownership chips, then
    /// Expansion with Source, then Sort with Group by. That is 3 rows (~150 px) back to the viewport without
    /// dropping a control.
    /// </para>
    /// <para>
    /// A pair only fits label-free, so the combos name their own dimension in the field ("All expansions",
    /// "Sort: Name") instead of carrying a label to their left: the label cost exactly the width the second
    /// control needs, and the field's own text is where the player reads the current filter anyway. Below
    /// <see cref="LayoutMeasure.FilterPairMinWidth"/> the pairs stack back into one column - at the narrow window
    /// a half-width field would truncate both combos rather than fit them - so the block is 7 rows there and 4
    /// everywhere else.
    /// </para>
    /// </remarks>
    private void DrawFilters(MountViewState state)
    {
        // The controls fill the column, and a hovered control grows past it (see PushControlRoom). The
        // push is paired in a finally: MainWindow keeps drawing after a failed frame, so a throw that
        // skipped the pop would leave the clip stack one short for the rest of the session (and pop a
        // clip the caller never pushed).
        PushControlRoom();

        try
        {
            var scale = Frame.Scale;
            var gap = Frame.Look.SpaceSmall * scale;
            var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
            var paired = LayoutMeasure.FilterPairFits(width / scale);
            var half = MathF.Max((width - gap) * 0.5f, 1f);

            // The presets own a line of their own above the search box, and no line at all when there are none
            // (see DrawSavedViewRow): the block's four rows are the space the list was given back.
            DrawSavedViewRow(state);

            // The switch keeps its natural width - two cells of its own widest label - so the search field gets
            // the rest of the row. It never takes more than half the row, so a long translation cannot starve
            // the field it shares the row with; on a column too narrow to pair it keeps the whole row instead,
            // because a 92 px switch alone on a 328 px row reads as a stray label rather than as a control.
            var switchWidth = paired ? MathF.Min(ViewSwitchWidth(scale), half) : width;

            ImGui.SetNextItemWidth(paired ? MathF.Max(width - switchWidth - gap, 1f) : width);

            var search = state.Search;
            _ = Gw.TextField(ref search, hint: Loc.T("filter.search.hint"));
            state.Search = search;

            // The operators exist but nothing on screen advertises them; a tooltip is the one hint that
            // costs no layout in any of the goldens.
            Gw.TooltipFor(ImGui.IsItemHovered(), SearchTooltip);

            // Cards or the dense table. It sits with the filters because it changes how the list is
            // drawn, not what is in it, and because a fifth header button cost the per-expansion strip
            // its room in a 1040 px window - the strip is a feature, the switch is a preference.
            var view = (int)state.ViewMode;

            if (paired)
            {
                ImGui.SameLine(0f, gap);
            }

            ImGui.SetNextItemWidth(switchWidth);

            if (Gw.Segmented(ref view, ViewLabels))
            {
                state.ViewMode = (MountViewMode)view;
            }

            var ownership = (int)state.Ownership;
            var chipLabels = RefreshOwnershipLabels(state);

            // Chips across the filter column, or one picker when the column cannot hold them. What the row needs
            // is measured, never budgeted: Gw.Segmented gives its cells equal widths, so the widest label sets
            // all four, and the labels grow with the counts the player sees - "Unowned 233" is 26 px wider than
            // "Unowned 3". The row used to change shape at a fixed 380 px column, a number unrelated to the label
            // and to the room the control actually had (it took ImGui's default item width, a fraction of the
            // column), so a bigger book drew cells too narrow and the label clipped to "Unown?".
            var chipInset = Frame.Look.SwitchKnobInset * scale;
            var chipSlack = Frame.Look.SpaceSmall * scale;

            // Both shapes take the whole column, so the row lines up with the ones above and below it.
            ImGui.SetNextItemWidth(width);

            // A chip that does not fit truncates rather than wraps, so below the measured width the control
            // changes shape: the picker is worse to use and better than "Unown?".
            if (LayoutMeasure.ChipsFit(width, chipLabels.Length, widestChipLabel, chipInset, chipSlack))
            {
                _ = Gw.Segmented(ref ownership, chipLabels);
            }
            else
            {
                _ = Gw.ComboBox(ref ownership, chipLabels, label: Loc.T("filter.show"));
            }

            state.Ownership = (MountOwnership)ownership;

            var expansion = (int)state.Expansion;
            ImGui.SetNextItemWidth(paired ? half : width);
            _ = Gw.ComboBox(ref expansion, ExpansionLabels);
            state.Expansion = (MountExpansion)expansion;

            // The obtain-type filter: "what do I still need from PvP" without a search that also matches lore.
            // Every entry carries the dataset's own count, so the combo answers "how much is left there" before
            // the player commits to a click.
            var source = SourceIndex(state);

            if (paired)
            {
                ImGui.SameLine(0f, gap);
            }

            ImGui.SetNextItemWidth(paired ? half : width);
            _ = Gw.ComboBox(ref source, SourceLabelsFor(state));
            state.SourceFilter = source >= 0 && source < state.SourceOptions.Count
                ? state.SourceOptions[source].Kind
                : MountSourceKinds.Any;

            // The availability filter: the question this feature exists for ("what can I go and get") as one
            // control, with the count each class would show. It sits under the obtain type because the two
            // answer different halves of the same question - what kind of thing it is, and whether that route is
            // open today. It pairs with the order when the grid's grouping combo is not drawn (the table), and
            // takes its own row when it is, which is the row the grid already spends on grouping.
            var groupingShown = state.ViewMode == MountViewMode.Grid;
            var availabilityShown = state.AvailabilityOptions.Count > 0;

            if (availabilityShown)
            {
                var availability = AvailabilityIndex(state);
                ImGui.SetNextItemWidth(paired && !groupingShown ? half : width);
                _ = Gw.ComboBox(ref availability, AvailabilityLabelsFor(state));
                state.AvailabilityFilter = AvailabilityFilterAt(state, availability);
            }

            // Grouping is a card-grid feature: the table already has a column for every fact the headings
            // would repeat, so the control only appears where it does something - and then it takes the
            // second half of the sort's row. Without it the sort keeps the whole row rather than leaving a
            // hole where a control the mode cannot use would sit.
            var order = (int)state.Order;

            if (availabilityShown && paired && !groupingShown)
            {
                ImGui.SameLine(0f, gap);
            }

            ImGui.SetNextItemWidth(paired && groupingShown ? half : width);
            _ = Gw.ComboBox(ref order, OrderLabels);
            state.Order = (MountOrder)order;

            if (groupingShown)
            {
                if (paired)
                {
                    ImGui.SameLine(0f, gap);
                }

                var grouping = (int)state.GroupBy;
                ImGui.SetNextItemWidth(paired ? half : width);
                _ = Gw.ComboBox(ref grouping, GroupLabels);
                state.GroupBy = (MountGrouping)grouping;
            }
        }
        finally
        {
            PopControlRoom();
        }
    }

    /// <summary>
    /// The width the Cards/Table switch needs to draw both labels whole: two cells of the wider label, each
    /// giving its ink the switch knob's inset on both sides, plus a small space of slack - the selected cell
    /// draws semibold, which is a fraction wider than the measurement.
    /// </summary>
    /// <param name="scale">The UI scale the switch will be drawn at.</param>
    private static float ViewSwitchWidth(float scale)
    {
        var inset = Frame.Look.SwitchKnobInset * scale;
        var widest = MathF.Max(
            TextWidth(Loc.T("view.cards"), TextRole.Subheadline, scale),
            TextWidth(Loc.T("view.table"), TextRole.Subheadline, scale));

        return MathF.Ceiling((2f * (widest + (inset * 2f))) + (Frame.Look.SpaceSmall * scale));
    }

    /// <summary>
    /// The combo index of the active source filter: 0 (the "any" entry) when the filter is not one of the
    /// options - a type the shelf no longer carries, which happens when the dataset moves under a saved filter.
    /// </summary>
    private static int SourceIndex(MountViewState state)
    {
        IReadOnlyList<MountSourceCount> options = state.SourceOptions;

        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].Kind == state.SourceFilter)
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>The source combo's labels, built from the entries' types and counts.</summary>
    private string[] sourceLabelText = [];

    /// <summary>The options <see cref="sourceLabelText"/> was built from, compared by reference.</summary>
    private IReadOnlyList<MountSourceCount>? sourceLabelSource;

    /// <summary>
    /// The labels the source combo draws. The entries are types, so the identity of a filter and the text the
    /// player reads are different things: the label is the type's table entry plus its count, rebuilt only when
    /// the window built a new set of entries.
    /// </summary>
    /// <param name="state">The state whose source options to label.</param>
    /// <returns>The display labels, in entry order.</returns>
    private string[] SourceLabelsFor(MountViewState state)
    {
        if (!ReferenceEquals(sourceLabelSource, state.SourceOptions))
        {
            sourceLabelSource = state.SourceOptions;
            sourceLabelText = new string[state.SourceOptions.Count];

            for (var index = 0; index < sourceLabelText.Length; index++)
            {
                sourceLabelText[index] = FormatSourceLabel(state.SourceOptions[index]);
            }
        }

        return sourceLabelText;
    }

    /// <summary>
    /// One combo entry: the type's translated label and how many mounts it would show. The count belongs in the
    /// label rather than in a column of its own because the combo is a single control - and it is the number
    /// that answers "what is left in Cosmic Exploration" without filtering to find out.
    /// </summary>
    /// <remarks>
    /// The combo has no label to its left (a second control shares its row), so the entry for the unfiltered
    /// state names the dimension - "Any source (12)" - and a picked type keeps its name and count. A prefix on
    /// every value would cost the field ~50 of the ~175 px it has at the minimum window of 820x520, and
    /// "Cosmic Exploration (3)" already reads as a source: the count is the number worth keeping.
    /// </remarks>
    /// <param name="option">The entry to label.</param>
    private static string FormatSourceLabel(MountSourceCount option) =>
        option.Kind.IsAny
            ? Loc.F("filter.source.any", option.Mounts)
            : Loc.F("filter.source.option", UiText.Source(option.Kind), option.Mounts);

    /// <summary>
    /// The combo index of the active availability filter: 0 (the "any" entry) when nothing is filtered, or when
    /// the class is one the shelf no longer carries - which happens when the dataset moves under a saved filter.
    /// </summary>
    /// <param name="state">The state whose availability options to search.</param>
    private static int AvailabilityIndex(MountViewState state)
    {
        if (state.AvailabilityFilter is not { } current)
        {
            return 0;
        }

        IReadOnlyList<MountAvailabilityCount> options = state.AvailabilityOptions;

        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].Class == current)
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>The class a combo index stands for, or null for the "any" entry and for an index past the list.</summary>
    /// <param name="state">The state whose options the index refers to.</param>
    /// <param name="index">The combo's selected index.</param>
    private static MountAvailability? AvailabilityFilterAt(MountViewState state, int index) =>
        index >= 1 && index <= state.AvailabilityOptions.Count
            ? state.AvailabilityOptions[index - 1].Class
            : null;

    /// <summary>The availability combo's labels, rebuilt only when the window built a new set of entries.</summary>
    private string[] availabilityLabelText = [];

    /// <summary>The options <see cref="availabilityLabelText"/> was built from, compared by reference.</summary>
    private IReadOnlyList<MountAvailabilityCount>? availabilityLabelSource;

    /// <summary>
    /// The labels the availability combo draws: the "any" entry first, then one per class the shelf carries with
    /// the count it would show - the same shape as the source combo, and for the same reason (the count answers
    /// "how much is left there" before the player commits to a click).
    /// </summary>
    /// <param name="state">The state whose availability options to label.</param>
    private string[] AvailabilityLabelsFor(MountViewState state)
    {
        IReadOnlyList<MountAvailabilityCount> options = state.AvailabilityOptions;

        if (!ReferenceEquals(availabilityLabelSource, options))
        {
            availabilityLabelSource = options;
            availabilityLabelText = new string[options.Count + 1];

            var total = 0;

            for (var index = 0; index < options.Count; index++)
            {
                total += options[index].Mounts;
            }

            availabilityLabelText[0] = Loc.F("filter.availability.any", total);

            for (var index = 0; index < options.Count; index++)
            {
                availabilityLabelText[index + 1] = Loc.F(
                    "filter.availability.option",
                    UiText.Availability(options[index].Class),
                    options[index].Mounts);
            }
        }

        return availabilityLabelText;
    }

    /// <summary>
    /// The least height the empty state can take and still name its one action: the heading, a gap and the
    /// button, with neither explanatory line. <see cref="DrawFilterColumn"/> compares the room the list has
    /// against it to decide whether the state can live in the list at all.
    /// </summary>
    private static float EmptyStateFloorHeight()
    {
        var scale = Frame.Scale;
        var look = Frame.Look;

        return (look.Type.LineHeight(TextRole.Title) * scale)
            + (look.SpaceSmall * scale)
            + (look.SpaceMedium * scale)
            + (MathF.Max(look.ControlHeight, look.MinHit) * scale);
    }

    /// <summary>
    /// The squeezed-window form of the empty state: the heading and its button on one row, above the filters,
    /// for a column the filters have already filled.
    /// </summary>
    /// <remarks>
    /// It keeps the same promise in less room - the state is named and exactly one action is offered - and it
    /// drops the explanatory lines entirely rather than truncating them: at this size the search box it would
    /// point at is itself scrolled out of the column, so a sentence about filters would be advice the player
    /// cannot see the target of.
    /// </remarks>
    private static void DrawCompactEmptyState(MountViewState state)
    {
        var scale = Frame.Scale;
        var onWantedList = state.Ownership == MountOwnership.Wanted;

        Gw.Text(Loc.T(onWantedList ? "empty.wanted.title" : "empty.match.title"), TextRole.Subheadline);
        ImGui.SameLine(0f, Frame.Look.SpaceSmall * scale);

        if (Gw.Button(Loc.T(onWantedList ? "empty.wanted.button" : "empty.match.button"), ButtonStyle.Default, ButtonSize.Small))
        {
            state.ClearFiltersRequested = true;
        }
    }

    /// <summary>
    /// The no-results state: a heading, one line naming the next action, and the button that takes it.
    /// Placed about a third of the way down the card rather than at the very top, so it reads as a
    /// state of the list instead of a stray label.
    /// </summary>
    /// <remarks>
    /// The block is measured before it is placed. The old shape reserved "a title, two footnote lines and a
    /// button" as a constant, which is one Look at one UI scale: the hint and the operator line both wrap,
    /// and at the window's 820x520 minimum the filter column leaves the list under a hundred pixels, so the
    /// button was drawn on top of the hint and clipped by the child. What yields is the grammar line, then
    /// the hint - the button is the one action the state names and it is the last thing to go.
    /// </remarks>
    private static void DrawEmptyState(MountViewState state, float height)
    {
        var scale = Frame.Scale;
        var look = Frame.Look;

        // An empty *wanted* list is not a filter problem: it is a list with nothing on it yet, and the hint
        // should say how to add something rather than how to widen a filter.
        var onWantedList = state.Ownership == MountOwnership.Wanted;
        var hint = Loc.T(onWantedList ? "empty.wanted.hint" : "empty.match.hint");
        var action = Loc.T(onWantedList ? "empty.wanted.button" : "empty.match.button");
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);

        var gap = look.SpaceSmall * scale;

        var headingHeight = look.Type.LineHeight(TextRole.Title) * scale;
        var hintHeight = TextPaint.MeasureWrapped(hint, TextRole.Footnote, in look, scale, width, 0).Y;

        // An empty list is where a player learns the grammar: the same sentence the search field's
        // tooltip carries, so "no mounts match" also says how to ask a better question. A wanted list has
        // no grammar to teach, so it has no line at all.
        var grammarHeight = onWantedList
            ? 0f
            : TextPaint.MeasureWrapped(SearchHint, TextRole.Footnote, in look, scale, width, 0).Y;

        // Gw reserves an item as Max(drawn size, Look.MinHit), so a button row is MinHit tall, not
        // ControlHeight - reserving the smaller one is a constant few pixels short and reads as a clipped
        // button rather than as arithmetic.
        var buttonHeight = MathF.Max(look.ControlHeight, look.MinHit) * scale;

        float BlockHeight(bool withHint, bool withGrammar) =>
            headingHeight
            + gap
            + (withHint ? hintHeight + gap : 0f)
            + (withGrammar ? grammarHeight + gap : 0f)
            + (look.SpaceMedium * scale)
            + buttonHeight;

        var showHint = BlockHeight(true, false) <= height;
        var showGrammar = !onWantedList && showHint && BlockHeight(true, true) <= height;
        var blockHeight = BlockHeight(showHint, showGrammar);

        var at = ImGui.GetCursorScreenPos();
        var top = at.Y + MathF.Max((height - blockHeight) * 0.5f, 0f);

        ImGui.SetCursorScreenPos(new Vector2(at.X, top));
        Gw.Heading(Loc.T(onWantedList ? "empty.wanted.title" : "empty.match.title"));

        if (showHint)
        {
            Gw.TextWrapped(hint, TextRole.Footnote);
        }

        if (showGrammar)
        {
            Gw.TextWrapped(SearchHint, TextRole.Footnote, look.TertiaryLabel);
        }

        ImGui.Dummy(new Vector2(0f, look.SpaceMedium * scale));

        if (Gw.Button(action, ButtonStyle.Default, ButtonSize.Small))
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
        // SpaceSmall is the visual margin; HaloRoom is the extra room for the hover and selection halos,
        // which paint outside the card and were being clipped by the child's clip rectangle on the outer
        // columns. Both are logical, scaled once here.
        var inset = (Frame.Look.SpaceSmall + HaloRoom) * scale;
        // The scrollbar is drawn inside the child's content region, so the right inset is only real
        // if its width is reserved as well - otherwise the last column hugs the bar.
        var scrollbar = ImGui.GetScrollMaxY() > 0f ? ImGui.GetStyle().ScrollbarSize : 0f;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(inset, 0f);
        var available = MathF.Max(ImGui.GetContentRegionAvail().X - (inset * 2f) - scrollbar, 1f);
        var perRow = Math.Max(1, (int)((available + minGutter) / (cellPx + minGutter)));
        var gutter = perRow > 1 ? MathF.Max(minGutter, (available - (perRow * cellPx)) / (perRow - 1)) : 0f;

        var pitchX = cellPx + gutter;

        // Every row was pitched for two caption lines. A row whose names all fit on one line therefore
        // reserved ~16 px it never drew - 14% of a short row - so the pitch is now the row's own tallest
        // caption. The width is the one DrawCard measures its caption at, so the two cannot disagree.
        var captionWidth = cellPx - (Frame.Look.SpaceExtraSmall * 2f * scale);
        var singleRowHeight = iconPx + captionGap + lineHeight + rowGap;
        var doubleRowHeight = singleRowHeight + lineHeight;

        // The hover ease advances once per frame, from the card the pointer was over last frame. It used
        // to advance inside the card body, which multiplied it by (1 - easing) for every card drawn
        // before the hovered one: with sixty cards the glow was already under the visible threshold by
        // the middle of the grid, so only the last row ever lit up.
        cardLift += ((hoveredCardId != 0 ? 1f : 0f) - cardLift) * MathF.Min(1f, ImGui.GetIO().DeltaTime * 14f);
        hoveredCardId = 0;

        // The sections share the same lattice as the flat grid, one run of rows each: grouping changes
        // where a row starts, never the column pitch, so the tiles stay in one lattice either way.
        // Built once per (list, grouping) rather than once per frame: Build allocates a dictionary, a list per
        // heading and a titles list, and sorts them, for the whole filtered shelf. The list is the same reference
        // until the window re-runs the query, which is the same signal the window's own caches key on.
        if (cachedGroups is null
            || !ReferenceEquals(cachedGroupsSource, state.Items)
            || cachedGroupsBy != state.GroupBy)
        {
            cachedGroups = MountGroups.Build(state.Items, state.GroupBy);
            cachedGroupsSource = state.Items;
            cachedGroupsBy = state.GroupBy;
        }

        IReadOnlyList<MountGroup> groups = cachedGroups;
        var headerPitch = (Frame.Look.Type.LineHeight(TextRole.Caption) + Frame.Look.SpaceMedium) * scale;
        var y = origin.Y;

        // Only the rows the viewport can show are drawn. The grid used to declare all 369 cards
        // every frame: the scroll child clipped the paint, but each card still paid its glass
        // capture, two texture lookups and - on the first frame, where nothing is memoised - its
        // text measurements. That was 163 ms of the 213 ms the client logged as a UiBuilder hitch
        // on load. The walk still advances y for every row, so the scroll range is unchanged.
        // One row of margin either side keeps a partly visible row drawing instead of popping in.
        var visibleMin = ImGui.GetWindowPos();
        var visibleMax = visibleMin + ImGui.GetWindowSize();
        var visibleTop = visibleMin.Y - doubleRowHeight;
        var visibleBottom = visibleMax.Y + doubleRowHeight;

        foreach (MountGroup group in groups)
        {
            if (group.Title.Length > 0)
            {
                if (y >= visibleTop && y <= visibleBottom)
                {
                    // The tile inside a cell is centred in it (see DrawCard's tileAt), so a heading flush
                    // with origin.X stood 28 px to the left of every card in its section. It shares the tile's edge.
                    ImGui.SetCursorScreenPos(new Vector2(origin.X + ((cellPx - iconPx) * 0.5f), y));
                    Gw.Text(UiText.GroupTitle(group.Title, state.GroupBy), TextRole.Caption, Frame.Look.SecondaryLabel);
                    ImGui.SameLine(0f, Frame.Look.SpaceSmall * scale);
                    Gw.Text(FormatInt(group.Items.Count), TextRole.Caption, Frame.Look.TertiaryLabel);
                }

                // The heading's pitch belongs to the scroll range whether or not it is drawn.
                y += headerPitch;
            }

            for (var start = 0; start < group.Items.Count; start += perRow)
            {
                int rowEnd = Math.Min(start + perRow, group.Items.Count);
                var twoLine = false;

                for (var index = start; index < rowEnd; index++)
                {
                    if (CaptionFor(group.Items[index], captionWidth, scale).Second.Length > 0)
                    {
                        twoLine = true;
                        break;
                    }
                }

                // The row's own pitch is what the scroll range is built from, so the height is
                // advanced whether or not the row is drawn.
                var rowHeight = twoLine ? doubleRowHeight : singleRowHeight;

                if (y >= visibleTop && y <= visibleBottom)
                {
                    for (var index = start; index < rowEnd; index++)
                    {
                        var cell = new Vector2(origin.X + ((index % perRow) * pitchX), y);

                        DrawCard(state, group.Items[index], cell, cellPx, iconPx, lineHeight, captionGap);
                    }
                }

                y += rowHeight;
            }
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
            // it on both sides (see the `inset` DrawGrid passes to DrawCard) - without that the outer
            // columns were cut off.
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
        // The cursor is a pixel API and Gw.Image takes logical units, so the inset is converted
        // for the position and kept logical for the size. Subtracting a pixel inset from a logical
        // size drew the pressed tile scale-squared smaller and off-centre inside its own selection
        // ring, which is still drawn at the full tile.
        var pressInset = pressed ? 2f * Frame.Scale : 0f;
        var pressInsetLogical = pressed ? 2f : 0f;

        ImGui.SetCursorScreenPos(tileAt + new Vector2(pressInset, pressInset));
        Gw.Image(
            TextureFor(state, item.IconId),
            new Vector2(IconSize - (pressInsetLogical * 2f), IconSize - (pressInsetLogical * 2f)),
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
            Gw.TooltipFor(freshHovered, Loc.T("card.recent"));
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

        Gw.TooltipFor(markerHovered, wanted ? Loc.T("card.want.remove") : Loc.T("card.want.add"));

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
                    ? Loc.F(
                        "card.tooltip.owned",
                        item.Name,
                        Loc.T("card.ride"))
                    : Loc.F(
                        "card.tooltip.unowned",
                        item.Name,
                        Loc.T("card.uncollected")));
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

    /// <summary>How many wrapped captions are kept before the map is dropped; see <see cref="CaptionFor"/>.</summary>
    private const int CaptionCacheLimit = 1024;

    /// <summary>
    /// The caption to draw for a card: word-wrapped to at most two lines, each measured with the
    /// renderer's own text paint, and shortened with an ellipsis only when two lines still overflow.
    /// </summary>
    private static Caption CaptionFor(MountShelfItem item, float width, float scale)
    {
        // The width is pixels, so the scale belongs in the key: the same logical cell can produce the
        // integer width a different scale did, and the stored widths are what the caption is centred
        // and clipped against.
        var key = (item.Id, (int)MathF.Round(scale * 1000f), (int)width);
        if (Captions.TryGetValue(key, out Caption cached))
        {
            return cached;
        }

        var caption = Wrap(item.Name, width, scale);

        // A higher cap than the shared 512, on purpose: a caption is keyed by the pixel width it was wrapped to,
        // so dragging the window edge through a few pixels creates a whole new generation of entries. 512 would
        // clear the map mid-drag, when re-measuring every visible caption is exactly what the cache is for.
        if (Captions.Count > CaptionCacheLimit)
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
    /// measurement came out narrow and names were cut just before the ellipsis. Memoised per text, because a
    /// fit asks it about every candidate truncation.
    /// </summary>
    private static float CaptionWidth(string text, float scale) => TextWidth(text, TextRole.Callout, scale);

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
