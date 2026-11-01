using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Draws the main window's body from <see cref="MountViewState"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// No Dalamud service is touched here, no configuration, and no wall clock: the window gathers the
/// mounts and the player's progress into the state first, and the headless preview draws this same
/// view with fixed sample data (CONTRIBUTING T6, T7). Anything that differs between two runs - the
/// player's collection, the game's data - is already a value in the state.
/// </para>
/// <para>
/// The body sits in a <c>Gw.BeginCard()</c> panel rather than straight on the window, which is what
/// makes it readable both over the glass in game and over the preview's backdrop stand-in. Colour is
/// never painted by hand: every widget and every highlight takes its ink from the Look (N9). The two
/// columns and the scrolling card grid use plain ImGui children - layout only. Glasswork does ship
/// scrolling components (<c>VirtualList</c>, surface <c>Scroll</c>, <c>Gw.BeginSplit</c>), but they
/// impose their own item model; the grid here is a plain child, and every control inside it is still
/// a <c>Gw</c> widget.
/// </para>
/// </remarks>
public sealed partial class MountbookView
{
    /// <summary>
    /// The distance <c>Gw.EndCard()</c> leaves under the columns, in screen pixels, measured on an earlier
    /// frame; not positive until the first measurement lands. It used to be a hand-measured 16 logical px
    /// constant, which was one Look's large spacing at one UI scale and drifted the moment either moved.
    /// </summary>
    private float cardBottomInsetPx = -1f;

    /// <summary>The UI scale <see cref="cardBottomInsetPx"/> was measured at; a scale change re-measures it.</summary>
    private float cardBottomInsetScale = -1f;

    /// <summary>
    /// The pane's bottom edge in screen space, taken at the top of <see cref="Draw"/> before the card child
    /// opens. See <see cref="LayoutMeasure.ColumnHeight"/> for why neither the reported content region nor a
    /// child's clip rect can be trusted for this.
    /// </summary>
    private float paneBottomScreenY;


    /// <summary>One card's width: the 64 px tile plus room for a readable caption.</summary>
    private const float CellWidth = 120f;

    /// <summary>
    /// What the search box accepts beyond plain words. One string, because the tooltip and the empty
    /// state both advertise it and two copies drift - the operator list is the whole discoverability of
    /// the grammar.
    /// </summary>
    private static string SearchHint => Loc.T("search.hint");

    /// <summary>Room outside a tile for its hover and selection halos, logical units.</summary>
    private const float HaloRoom = 6f;

    /// <summary>Fitted captions and their measured width, keyed by mount and cell width.</summary>
    private static readonly System.Collections.Generic.Dictionary<(uint Id, int Scale, int Width), Caption> Captions = [];

    /// <summary>
    /// Measured text widths, keyed by the text, the style it draws in and the UI scale. The detail pane used
    /// to measure its title, the rarity clause, the market price and up to nine badges on every frame; a width
    /// only changes when the Look's type face does, which is exactly what <c>MeasurementGeneration</c> names.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(string Text, TextStyle Style, int Scale), float> TextWidths = [];

    /// <summary>
    /// Text already trimmed to a width, keyed the same way plus the floored width. Fitting walks the string a
    /// character at a time, so remembering the answer is what keeps the draw path free of per-frame work.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(string Text, TextStyle Style, int Scale, int Width), string> FittedText = [];

    /// <summary>
    /// The two-column header's sheet-fact line, keyed by the facts it is built from. The draw path must not
    /// build strings, and most mounts share their combination, so this settles after a few dozen entries.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(string Patch, int Seats, bool Barding, bool Flies), string> SheetFactLines = [];

    /// <summary>Small-button widths already summed, keyed by label and scale.</summary>
    private static readonly System.Collections.Generic.Dictionary<(string Label, int Scale), float> SmallButtonWidths = [];

    /// <summary>How many entries a measurement memo may hold before it is dropped and refilled.</summary>
    /// <remarks>
    /// Browsing the whole book asks about a few hundred distinct strings, so the cap is only a guard against a
    /// future caller that builds its labels from the frame; clearing is cheaper than an eviction policy here.
    /// </remarks>
    private const int MeasureCacheLimit = 512;

    /// <summary>The share of the card the filter and card column takes; the detail pane takes the rest.</summary>
    private const float LeftColumnShare = 0.62f;

    /// <summary>The icon tile edge inside a card, in logical units.</summary>
    private const float IconSize = 64f;

    /// <summary>The hero artwork's height in the detail pane, in logical units.</summary>
    private const float HeroHeight = 168f;

    /// <summary>
    /// Labels for <c>Gw.Segmented</c>, in <see cref="MountOwnership"/> order. Static arrays, because
    /// the widgets re-read them every frame and the draw path must not allocate. They are read once, at
    /// the first draw: the interface language is fixed for the session (<see cref="Loc.Use"/> runs in the
    /// plugin's constructor), so there is nothing to invalidate.
    /// </summary>
    private static readonly string[] OwnershipNames =
    [
        Loc.T("ownership.all"),
        Loc.T("ownership.owned"),
        Loc.T("ownership.unowned"),
        Loc.T("ownership.wanted"),
    ];

    /// <summary>Cached chip labels with their counts; rebuilt only when the counts change.</summary>
    private readonly string[] ownershipLabels = (string[])OwnershipNames.Clone();

    /// <summary>How many <see cref="PushControlRoom"/> clips are open, so a pop never underflows.</summary>
    private static int roomPushes;

    /// <summary>Cached strip labels, one per expansion, rebuilt when <see cref="expansionCounts"/> moves.</summary>
    private string[] expansionLabels = [];

    /// <summary>The owned/total pair each cached strip label was built from.</summary>
    private int[,] expansionCounts = new int[0, 2];
    private readonly int[] ownershipCounts = { -1, -1, -1, -1 };


    /// <summary>Labels for the expansion <c>Gw.ComboBox</c>, in <see cref="MountExpansion"/> order.</summary>
    /// <remarks>
    /// The combo carries no label of its own - a second control shares its row - so the entry for the unfiltered
    /// state names the dimension instead: "All expansions". A picked expansion keeps its own name, because
    /// "Heavensward" beside "Any source (12)" is a filter value, not a mystery, and a prefix on every value cost
    /// ~50 px of a ~215 px field (measured: "Expansion: Shadowbringers" is 215.4 px against the 175 px a
    /// half-width field has at the minimum window) for a word the default already taught. The order and grouping
    /// combos do prefix every value: "Name" and "Patch" are ambiguous without their dimension, "Stormblood" is not.
    /// </remarks>
    private static readonly string[] ExpansionLabels =
    [
        Loc.T("filter.expansion.all"),
        Loc.T("expansion.arr"),
        Loc.T("expansion.hw"),
        Loc.T("expansion.sb"),
        Loc.T("expansion.shb"),
        Loc.T("expansion.ew"),
        Loc.T("expansion.dt"),
        Loc.T("expansion.unknown"),
    ];

    /// <summary>The two ways the shelf can draw the same list.</summary>
    private static readonly string[] ViewLabels = [Loc.T("view.cards"), Loc.T("view.table")];

    /// <summary>Labels for the "Group by" <c>Gw.ComboBox</c>, in <see cref="MountGrouping"/> order.</summary>
    /// <remarks>Label-free like the others: "No grouping" names the state, "Group: Expansion" names the dimension.</remarks>
    private static readonly string[] GroupLabels =
    [
        Loc.T("group.none"),
        Loc.F("filter.group.value", Loc.T("group.expansion")),
        Loc.F("filter.group.value", Loc.T("group.patch")),
        Loc.F("filter.group.value", Loc.T("group.source")),
    ];

    /// <summary>Labels for the order <c>Gw.ComboBox</c>, in <see cref="MountOrder"/> order.</summary>
    /// <remarks>
    /// "Surprise me" is the one order that is a verb: it exists so the top of the list is a mount nobody
    /// would have picked, and saying so reads better than "Random", which sounds like a sort key.
    /// </remarks>
    private static readonly string[] OrderLabels =
    [
        Loc.F("filter.order.value", Loc.T("order.name")),
        Loc.F("filter.order.value", Loc.T("order.oldest")),
        Loc.F("filter.order.value", Loc.T("order.seats")),
        Loc.F("filter.order.value", Loc.T("order.ownedFirst")),
        Loc.F("filter.order.value", Loc.T("order.newest")),
        Loc.F("filter.order.value", Loc.T("order.rarest")),
        Loc.F("filter.order.value", Loc.T("order.cheapest")),
        Loc.F("filter.order.value", Loc.T("order.surprise")),
        Loc.F("filter.order.value", Loc.T("order.commonest")),
    ];

    /// <summary>The Look generation the cached measurements were taken at.</summary>
    private int measuredGeneration = int.MinValue;

    /// <summary>The UI scale the cached measurements were taken at: a scale change invalidates them too.</summary>
    private float measuredScale = -1f;

    /// <summary>
    /// Drops every cached text measurement in the view. Each one - the header action widths, the counts
    /// line, the expansion labels, the card captions - was measured with the Look's type face, so after a
    /// theme with a different face the advance widths no longer match what is drawn: the chrome drifts
    /// off the card edge and captions wrap or ellipsise against the previous font.
    /// </summary>
    private void InvalidateMeasurements()
    {
        actionWidths = [];
        actionWidthsScale = -1f;

        countedOwned = -1;
        countedTotal = -1;
        countedWanted = -1;
        countedWatching = -1;
        countedRarest = null;
        rarestMeasuredScale = -1f;

        filteredOwned = -1;
        filteredShown = -1;

        expansionLabels = [];
        expansionWidths = [];
        expansionCounts = new int[0, 2];

        Array.Fill(ownershipCounts, -1);

        // Static: the caption cache is shared by every view instance (and by the previews).
        Captions.Clear();
        TextWidths.Clear();
        FittedText.Clear();
        SmallButtonWidths.Clear();
        SheetFactLines.Clear();

        // The card's inset belongs to the same generation: a Look can bring its own spacing.
        cardBottomInsetPx = -1f;
        cardBottomInsetScale = -1f;

        // The four action labels are measured under the Look's type face too, so their widths are as stale as
        // everything above after a theme change at the same UI scale - and ActionRowCount decides from them
        // whether the actions fit on one line.
        detailActionWidths = null;
        detailActionWidthsScale = -1f;
    }

    private int filteredOwned = -1;

    private int filteredShown = -1;

    private string filterText = string.Empty;

    private int countedOwned = -1;
    private int countedTotal = -1;
    private string countText = string.Empty;
    private string percentText = string.Empty;
    private string remainingText = string.Empty;

    private int countedWanted = -1;

    /// <summary>The watched count the badge text was last built for, so the string table is not hit per frame.</summary>
    private int countedWatching = -1;
    private string wantedText = string.Empty;
    private string watchingText = string.Empty;

    /// <summary>
    /// Grants a hover's worth of room around the current window's clip rectangle.
    /// </summary>
    /// <remarks>
    /// Glasswork scales a hovered control up around its centre (<c>Look.HoverLift</c>), so a control whose
    /// edge coincides with a clip rectangle - a combo filling a column, a right-aligned header action, an
    /// action row at the bottom of its footer - paints a few pixels past it and loses its cap. The caller
    /// pairs this with <see cref="PopControlRoom"/> around the controls it protects. Nothing moves; the
    /// clip is simply allowed to be a little larger than the window.
    /// </remarks>
    private static void PushControlRoom()
    {
        var pad = 12f * Frame.Scale;

        if (pad <= 0f)
        {
            return;
        }

        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        roomPushes++;

        ImGui.GetWindowDrawList().PushClipRect(min - new Vector2(pad, pad), max + new Vector2(pad, pad), false);
    }

    /// <summary>Closes the clip <see cref="PushControlRoom"/> opened, if it opened one.</summary>
    private static void PopControlRoom()
    {
        if (roomPushes <= 0)
        {
            return;
        }

        roomPushes--;
        ImGui.GetWindowDrawList().PopClipRect();
    }

    /// <summary>
    /// Draws the whole body from <paramref name="state"/>, and reports clicks back into it.
    /// </summary>
    /// <remarks>
    /// <see cref="MountViewState.RideRequested"/>, <see cref="MountViewState.LinkRequested"/>,
    /// <see cref="MountViewState.GuideRequested"/> and <see cref="MountViewState.ToggleWantedId"/>
    /// are cleared at the start of every draw, so they are true (or set) for exactly one frame after
    /// a click. The caller must read them after this call, never cache them.
    /// </remarks>
    public void Draw(MountViewState state)
    {
        // Every cached measurement below is taken with the Look's type face (T6/T7), and a theme switch
        // can swap that face under us. The window bumps the generation whenever it applies a Look, so
        // the caches are dropped here, once, instead of being trusted across a theme change.
        if (state.MeasurementGeneration != measuredGeneration)
        {
            measuredGeneration = state.MeasurementGeneration;
            InvalidateMeasurements();
        }

        // A UI-scale change has to drop them for the same reason, and the generation does not move for
        // it: the header's count, percentage and expansion-strip widths are pixels measured at whatever
        // scale was in force, and the strip's fit test is what decides whether a shipped feature draws
        // inline or drops under the counters. Goldens render at scale 1, so only a live GlobalUiScale
        // change can show this - which is exactly why it was never seen.
        if (measuredScale != Frame.Scale)
        {
            measuredScale = Frame.Scale;
            InvalidateMeasurements();
        }

        // One list, kept next to the properties it resets: the window reads these after Draw returns.
        state.ClearRequests();

        // The pane's bottom edge in screen space, captured at the top of the frame before the card child
        // opens. Both the card's reported region and its clip rect describe the child, which for an
        // auto-sized card grows with its content and runs past the window (measured 842 for a 660-tall pane);
        // at window level, before any child, the reported region is the window's own.
        paneBottomScreenY = ImGui.GetCursorScreenPos().Y + ImGui.GetContentRegionAvail().Y;

        var columnsBottomScreenY = -1f;

        if (Gw.BeginCard())
        {
            DrawHeader(state);

            Gw.Divider();

            columnsBottomScreenY = DrawColumns(state);
        }

        Gw.EndCard();

        MeasureCardInset(columnsBottomScreenY);

        state.Committed = Gw.Committed;

        // After the card's own measurement and after the commit read: a dialog draws in its own window, so the
        // item rect it leaves behind is not the card's bottom edge, and a button pressed inside it is not a
        // value control's edit.
        DrawSavedViewsDialog(state);
    }

    /// <summary>
    /// Records how much room <c>Gw.EndCard()</c> leaves under the columns, from the item rects either side
    /// of it: the card lays one last item down ending at its own bottom edge, so the pair is the inset
    /// itself - no spacing arithmetic, no Look token guessed twice, and it follows the UI scale.
    /// </summary>
    /// <param name="columnsBottomScreenY">The columns' bottom edge, or a negative value when no card drew.</param>
    private void MeasureCardInset(float columnsBottomScreenY)
    {
        if (columnsBottomScreenY <= 0f)
        {
            return;
        }

        var inset = ImGui.GetItemRectMax().Y - columnsBottomScreenY;

        // A zero or inverted difference means EndCard drew no card of its own (it skips when nothing inside
        // could be visible), so the rects describe two different things and the reading is not one.
        if (inset <= 0f)
        {
            return;
        }

        cardBottomInsetPx = inset;
        cardBottomInsetScale = Frame.Scale;
    }

    /// <summary>
    /// The inset to leave under the columns right now: the measured value once this UI scale has one, and
    /// the card's own padding (the Look's large spacing) until then. Both are the same number by
    /// construction; the measurement is what keeps it true when Glasswork or the scale changes.
    /// </summary>
    private float CardInset =>
        LayoutMeasure.CardInset(
            cardBottomInsetScale == Frame.Scale ? cardBottomInsetPx : -1f,
            Frame.Look.SpaceLarge * Frame.Scale);

    private float DrawColumns(MountViewState state)
    {
        var available = ImGui.GetContentRegionAvail();
        var gap = Frame.Look.SpaceLarge * Frame.Scale;

        // Gw.EndCard() pads the card by the Look's large spacing under our content, so sizing the columns to
        // the raw available height overruns the window by that much and gives the window a scrollbar; the
        // padding is measured rather than assumed, because it follows both the Look and the UI scale. The
        // available height is only an upper bound: the card is an auto-sized child, so its content region
        // grows with its content and reports more than the window has (measured 661-715 in a 660-tall
        // window), which made the body taller than the pane and slid its last rows under the footer.
        var height = LayoutMeasure.ColumnHeight(
            paneBottomScreenY,
            ImGui.GetCursorScreenPos().Y,
            CardInset,
            available.Y);
        var leftWidth = MathF.Max((available.X * LeftColumnShare) - gap, 1f);

        DrawFilterColumn(state, leftWidth, height);

        // Both columns are children of this height, so either one's bottom edge is the card's content bottom;
        // the maximum covers the one scene where the pane draws no child of its own - the no-selection
        // placeholder - and the shorter text item would otherwise be read as the bottom of the card.
        var bottomScreenY = ImGui.GetItemRectMax().Y;

        ImGui.SameLine(0f, gap);

        DrawDetailColumn(state, height, MathF.Max(available.X - (leftWidth + gap), 1f));

        return MathF.Max(bottomScreenY, ImGui.GetItemRectMax().Y);
    }

    /// <summary>
    /// Tints the stock ImGui scrollbar from the Look. Glasswork only styles scrollbars inside its own
    /// table and surface components, so a plain child keeps ImGui's default grey bar; the colours here
    /// are still Look tokens (N9), just pushed where the library has no opinion.
    /// </summary>
    private static void PushScrollbarInk()
    {
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Frame.Look.Background.WithAlpha(0f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Frame.Look.Accent.WithAlpha(0.28f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Frame.Look.Accent.WithAlpha(0.48f).ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Frame.Look.Accent.WithAlpha(0.68f).ToVector4());
    }

    private static void PopScrollbarInk() => ImGui.PopStyleColor(4);

    /// <summary>
    /// The position of <see cref="MountViewState.SelectedId"/> in the drawn list, or -1 when nothing
    /// is selected or the selection was filtered out of the list.
    /// </summary>
    private static int SelectedIndex(MountViewState state)
    {
        if (state.SelectedId is not { } id)
        {
            return -1;
        }

        for (var index = 0; index < state.Items.Count; index++)
        {
            if (state.Items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A caption wrapped to at most two lines, with each line's measured width.</summary>
    private readonly record struct Caption(string First, string Second, float FirstWidth, float SecondWidth);

    /// <summary>
    /// The four action labels' widths under the current Look, cached per UI scale. Measured, not budgeted:
    /// the row was laid out against two hand-recorded constants (360 for the three original actions, 470 with
    /// the wanted toggle) that were taken at a Look's Default button size, and they kept the row on two lines
    /// after the secondaries shrank. <see cref="InvalidateMeasurements"/> drops this with every other
    /// measurement, because the widths belong to the Look's type face.
    /// </summary>
    private static float[]? detailActionWidths;

    /// <summary>The UI scale <see cref="detailActionWidths"/> was measured at.</summary>
    private static float detailActionWidthsScale;

    /// <summary>
    /// The width of <paramref name="text"/> in <paramref name="style"/>, measured once per text, style and
    /// scale and dropped with the other measurements when a Look is applied.
    /// </summary>
    private static float TextWidth(string text, TextStyle style, float scale)
    {
        var key = (text, style, (int)MathF.Round(scale * 1000f));

        if (TextWidths.TryGetValue(key, out var width))
        {
            return width;
        }

        var look = Frame.Look;
        width = TextPaint.Measure(text, style, in look, scale).X;
        Remember(TextWidths, key, width);
        return width;
    }

    /// <summary>Stores a measurement, dropping the whole memo first if it has grown past its cap.</summary>
    private static void Remember<TKey, TValue>(
        System.Collections.Generic.Dictionary<TKey, TValue> memo,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        if (memo.Count >= MeasureCacheLimit)
        {
            memo.Clear();
        }

        memo[key] = value;
    }

    /// <summary>
    /// The width <c>Gw.Button(ButtonSize.Small)</c> reserves for a label under the given Look: the footnote
    /// role at semibold, one medium space each side, never narrower than the small control height. One
    /// formula, shared by every place that has to decide whether two small buttons fit on a line. The price
    /// and wiki labels ask for theirs on every frame, so the sum is remembered per label and scale.
    /// </summary>
    private static float SmallButtonWidth(string label, Look look, float scale)
    {
        var key = (label, (int)MathF.Round(scale * 1000f));

        if (SmallButtonWidths.TryGetValue(key, out var width))
        {
            return width;
        }

        width = MathF.Max(
            TextWidth(label, ActionLabelStyle, scale) + (look.SpaceMedium * scale * 2f),
            look.ControlHeightSmall * scale);
        Remember(SmallButtonWidths, key, width);
        return width;
    }

    /// <summary>
    /// The four detail actions' widths on one line: Ride, Link whistle, Mount Guide, Want/Unwant. Measured
    /// from the labels under the Look's type face and cached per scale, because the old 470/360 budgets were
    /// recorded for the Default button size and kept the row at two lines after the secondaries shrank.
    /// "Unwant" is measured, not "Want": it is the wider of the two labels the toggle can draw.
    /// </summary>
    private static float[] DetailActionWidths(Look look, float scale)
    {
        if (detailActionWidths is { Length: 4 } && detailActionWidthsScale == scale)
        {
            return detailActionWidths;
        }

        Span<string> labels =
        [
            Loc.T("detail.ride"),
            Loc.T("detail.linkWhistle"),
            Loc.T("detail.mountGuide"),
            Loc.T("detail.unwant"),
        ];
        var widths = new float[4];

        for (var index = 0; index < labels.Length; index++)
        {
            var small = SmallButtonWidth(labels[index], look, scale);

            // Ride keeps ButtonStyle.Prominent: its own padding plus the taller control box. One large
            // space is the measured difference and it errs wide, which is the safe direction for a budget.
            widths[index] = index == 0 ? small + (look.SpaceLarge * scale) : small;
        }

        detailActionWidths = widths;
        detailActionWidthsScale = scale;
        return widths;
    }

    /// <summary>
    /// How many lines the detail actions need: 1 when all four share a line, 2 when the three older actions
    /// share one and the wanted toggle drops to the next, 4 when even that does not fit.
    /// </summary>
    private static int ActionRowCount(float available)
    {
        var look = Frame.Look;
        var scale = Frame.Scale;
        var widths = DetailActionWidths(look, scale);
        var gap = look.SpaceSmall * scale;

        if (available >= widths[0] + widths[1] + widths[2] + widths[3] + (gap * 3f))
        {
            return 1;
        }

        return available >= widths[0] + widths[1] + widths[2] + (gap * 2f) ? 2 : 4;
    }

    private string RefreshFilterCounts(MountViewState state)
    {
        var owned = 0;
        foreach (MountShelfItem item in state.Items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        if (owned == filteredOwned && state.Items.Count == filteredShown)
        {
            return filterText;
        }

        filteredOwned = owned;
        filteredShown = state.Items.Count;
        filterText = Loc.F(
            "filter.summary",
            FormatInt(owned),
            FormatInt(state.Items.Count),
            FormatInt(MountProgress.Percent(owned, state.Items.Count)));
        return filterText;
    }

    private static MountShelfItem? Selected(MountViewState state)
    {
        var index = SelectedIndex(state);
        return index < 0 ? null : state.Items[index];
    }

    private static nint TextureFor(MountViewState state, uint id) =>
        state.Textures.TryGetValue(id, out var handle) ? handle : (nint)0;

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static float Fraction(int owned, int total) =>
        total <= 0 ? 0f : Math.Clamp((float)owned / total, 0f, 1f);
}
