using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend;
using Mountbook.Services;

namespace Mountbook.Views;

/// <summary>
/// Part of <see cref="MountbookView"/>: the dense table, the same filtered list with one row per mount
/// for reading rather than browsing.
/// </summary>
/// <remarks>
/// <para>
/// The table is the scroll container for its own rows (<see cref="GwTableFlags.ScrollY"/>), so its heading
/// stays on screen while the body is scrolled. That is not decoration: a heading that leaves with the rows
/// makes sorting cost the reader their place, because the column can only be clicked at the top of the list.
/// </para>
/// <para>
/// The rows are in the order the caller produced, so the table never sorts anything itself: a header click
/// is translated into a <see cref="MountOrder"/> by <see cref="MountTable.OrderFor"/> and the window
/// re-queries. Two orderings would eventually disagree about what "oldest first" means, which is how the
/// patch parser ended up duplicated.
/// </para>
/// </remarks>
public sealed partial class MountbookView
{
    /// <summary>
    /// The sort the header drew on the last frame: the column its caret sat on and the direction it pointed,
    /// as the table reported them once the header row had been submitted.
    /// </summary>
    /// <remarks>
    /// The view's own bookkeeping, not the window's state: it describes a table, and the table belongs to
    /// the view. It exists so the sort spec can be written <em>before</em> the header draws rather than after
    /// it, and <see cref="NoSpecYet"/> is what makes the first frame write - ImGui opens a table on the spec
    /// it saved for that id in a previous session, and that spec describes the order that table was last
    /// left in, never the order the rows are in now.
    /// </remarks>
    private int headerColumn = NoSpecYet;

    /// <summary>Whether the caret the header drew on the last frame pointed down.</summary>
    private bool headerDescending;

    /// <summary>The column of a header that has never drawn a spec: no column at all, so the first frame writes.</summary>
    private const int NoSpecYet = -1;

    /// <summary>
    /// Draws the dense table: one table id, one set of column widths, and the rows in the order the caller
    /// put them in, in a body the table scrolls itself under a heading that stays where it is.
    /// </summary>
    /// <param name="state">The state whose items, selection and order the table draws.</param>
    /// <param name="height">
    /// The room the pane has left, in pixels, which is the table's outer height: the table is the scroll
    /// container, so a zero height here would make it as tall as its rows again and put the scroll back
    /// outside it.
    /// </param>
    private void DrawTable(MountViewState state, float height)
    {
        // ScrollY with a positive height is what freezes the heading: Glasswork's TableHeaders calls
        // ImGui.TableSetupScrollFreeze(0, 1) for that flag, so the header row is pinned above a body this
        // table scrolls. Without it the header is an ordinary first row of a table that grows to its full
        // content height (11852 px with the 369-mount book) inside whatever draws it, and the drawer moves
        // the whole thing - measured in the client at scroll.y 640, where the header row was off the top of
        // the `##mounts` child, so a heading could only ever be clicked at scroll 0.
        //
        // The scroll now belongs to the table, which is also why a sort keeps the reader's place: the scroll
        // is part of the state ImGui keeps per table id, and this table has one id for every order.
        const GwTableFlags Flags =
            GwTableFlags.Sortable
            | GwTableFlags.Resizable
            | GwTableFlags.Zebra
            | GwTableFlags.Selectable
            | GwTableFlags.ScrollY;

        // The pane's width and the room it has left, in logical units (the table scales its own size). A
        // zero size left the table as wide as its content - 675 px of a 1162 px pane - which the client
        // showed as a table that stopped two thirds of the way across; a zero height does the same thing to
        // the scroll, and that is the defect the flag above closes.
        var width = ImGui.GetContentRegionAvail().X / Frame.Scale;
        var bodyHeight = MathF.Max(height / Frame.Scale, 1f);

        // ONE id for every order, and the order is nowhere in it. ImGui keeps a table's state by id - the
        // widths it fitted to the cells, the sort spec it saves, the scroll it was left at - so the per-order
        // ids this used to choose (##mountsTable0 through 7, which stopped a restored spec leaking between
        // orders) also gave every order a table of its own, and every one of those tables measured and drew
        // a different split. Measured in the client against 369 rows: a click on Seats drew the Seats column
        // 84 px further right, the Source column 96 px further right and the Owned column 93 px further left
        // than the Name order had them, and clicking Name again restored the first geometry exactly, because
        // the ids each kept their own. That is the resize a player sees under the pointer - a table that
        // re-lays itself out instead of reordering its rows.
        //
        // What the per-order ids were for is now the spec written below: the table is told the column its
        // rows are sorted by, from the order, before the header row is submitted, on every frame the header
        // disagrees. One spec, always this order's, and no second table for ImGui to restore anything into -
        // which is the whole class of defect rather than a compensation for it.
        if (!Gw.BeginTable(MountTable.TableId, MountTable.ColumnCount, Flags, new Vector2(width, bodyHeight)))
        {
            return;
        }

        // Two columns share the slack (the name, which the player scans, and the source); everything
        // else fits its content. A stretched "Collected" header truncated to "Colle?" in the golden,
        // because a stretch weight at this width was narrower than its own label. With the price column
        // added the table has seven columns in a preview narrower than the client's pane: at 3:2 the name
        // survives and the source cell truncates, and at 2:3 the name truncates instead. The name wins -
        // a truncated mount name is ambiguous, while "Achieve..." still reads, and the detail pane has the
        // whole line.
        //
        // None of them is declared as the table's default sort any more, and that is deliberate: a declared
        // default carries no direction, so it could never draw the caret for a descending order, and ImGui
        // prefers its saved spec over it as soon as the id has one. The sort comes from MountTable.ColumnFor
        // and MountTable.Descends, written below, and nothing about a column depends on which order is drawn.
        Gw.TableColumn(Loc.T("table.name"), 3f, true);
        Gw.TableColumn(Loc.T("table.patch"), 0f, false);
        Gw.TableColumn(Loc.T("table.seats"), 0f, false, false, true);
        Gw.TableColumn(Loc.T("table.source"), 2f, true);

        // "Owned" is the player's own bit, drawn as one dot instead of the word "Yes"; "Owners" is the
        // share of collectors who have the mount, which the column had long called "Owned" while showing
        // a percentage nobody owned. The heading is what a fit-content column is sized by, so the dot's
        // column is as narrow as that five-letter word allows: 112 px of "Collected" was most of the
        // width the name and the source were short of.
        //
        // Owners stays numeric: a share is a number meant to be compared down the column, and a column of
        // left-aligned ones cannot be - a short share and a long one line up at neither edge, which is the
        // one thing a column of numbers must never do. A rare share's mark is now a fill painted behind
        // the number rather than a widget beside it (see DrawRarity), so no cell is wider than its digits
        // and the "Owners" heading that has always sized the column still does.
        Gw.TableColumn(Loc.T("table.owned"), 0f, false);
        Gw.TableColumn(Loc.T("table.owners"), 0f, false, false, true);

        // The Price column is declared at its widest from the first frame, which is what keeps it from
        // changing width as prices arrive. Prices are the one cell that appears after the table is already
        // on screen, and a fit-content column widened when the read landed - the name, patch, seats and
        // source all slid left under the pointer, and a column that also filled a chunk at a time looked
        // like it was flickering. A fixed width makes the column's shape a property of the table and not
        // of what has been answered so far. It is a column of numbers, so it right-aligns like Owners.
        Gw.TableColumn(
            Loc.T("table.price"),
            PriceColumnWidth(Frame.Look, Frame.Scale),
            false,
            false,
            true);

        // The sort spec, from the order, before the header row draws. Where it sits is the point:
        //
        // - the header's caret is drawn from the live spec, so writing it here is what makes the caret show
        //   this frame's order rather than the spec ImGui restored for this id (its saved one from the last
        //   session, or the first column ascending when it has none);
        // - and because a spec belongs to an id, the only spec that can be restored into this table is this
        //   table's own last write - which is exactly what this write corrects, before anything draws it.
        //
        // The comparison is against the pair read from the table on the previous frame, because a click
        // arrives during the header row: on the frame a click changes the order, this write has already
        // passed. It is skipped while the header already says what the rows say - a write marks the table's
        // settings dirty, and an unconditional one would rewrite the ini every frame.
        TableSort wanted = TableSort.At(state.Order);

        if (wanted.Column != headerColumn || wanted.Descending != headerDescending)
        {
            SetCaret(state.Order);
        }

        Gw.TableHeaders();

        // The report is read for its column and direction every frame, not only on the frame it returns
        // true: Gw.TableSortChanged fills those two from the live spec either way, and the report itself
        // only fires while the spec is dirty - on a click, on the table's first frame, or after ImGui drops
        // its transient buffers. A restored spec is not a click, so it is never believed as one
        // (MountTable.ClickedOrder asks whether a header was pressed), and since the write above has already
        // replaced a restored spec with this order's, a report that fires on a first frame reports our own
        // write rather than the ini's.
        bool changed = Gw.TableSortChanged(out int column, out bool descending);

        // One call decides the frame: the click's order, and the caret that order belongs under. It is a
        // pure function in Services/ because the rule is about the table's frames - a report that does not
        // fire when the spec merely changed outside a click - and not about ImGui, so the click sequences a
        // caret could get left behind by are pinned by TableSortTests without a renderer.
        TableSort sort = TableSort.Observe(state.Order, column, descending, changed, HeaderPressed());

        if (sort.Order != state.Order)
        {
            state.Order = sort.Order;
        }

        // The caret follows the rows, which are the single source of truth: the column that shows the
        // order, in the direction it sorts. This is the repair for the one frame the write above cannot
        // cover - a click, which ImGui applies to the spec during the header row, in its own direction
        // (ascending for a column that was not sorted) - and for a header that has no order of its own, which
        // asks for nothing but still leaves its spec behind. Writing here is visible on the next frame, which
        // is the same frame the rows change in, so the caret and the rows never disagree for longer than a
        // frame, and the write above keeps them agreeing from then on.
        headerColumn = sort.Column;
        headerDescending = sort.Descending;

        if (sort.Column != column || sort.Descending != descending)
        {
            SetCaret(sort.Order);
        }

        var accent = Frame.Look.Accent;
        var selected = state.SelectedId;

        for (var index = 0; index < state.Items.Count; index++)
        {
            MountShelfItem item = state.Items[index];

            if (Gw.TableRow(index, item.Id == selected))
            {
                state.SelectedId = item.Id;
            }

            // A wanted mount's name is the only tint in the table: the hunting list is the one thing
            // here that the player put there, and the detail pane has the marker itself.
            Gw.TableCell();
            Gw.TableText(item.Name, TextRole.Body, state.WantedIds.Contains(item.Id) ? accent : null);

            Gw.TableCell();
            Gw.TableText(MountTable.PatchText(item), TextRole.Footnote);

            Gw.TableCell();
            Gw.TableText(SeatsLabel(item), TextRole.Footnote);

            Gw.TableCell();
            Gw.TableText(SourceLabel(item), TextRole.Footnote);

            Gw.TableCell();
            DrawOwnedDot(item.Owned, accent);

            Gw.TableCell();
            DrawRarity(item, Frame.Look);

            // Empty until the prices are read: a column of dashes would read as "free", and the row
            // order already parks the unpriced mounts at the end when this is the sort column. Which rows
            // are worth a number is MountPriceColumn's call, the same one the window's fetch rule uses.
            Gw.TableCell();
            Gw.TableText(
                PriceLabel(MountPriceColumn.PriceOf(state.Prices, item.Id)),
                TextRole.Footnote);
        }

        // The body fades out at its own edges, as every scrolling child in this window does: inside the
        // table the current window is the body's own, so the fade reads that window's clip rect and scroll
        // exactly as it would in a child - a row sliding under the frozen heading dims instead of being cut,
        // and the heading itself is untouched, because the table draws it outside this window.
        Gw.EdgeFade();

        Gw.EndTable();
    }

    /// <summary>
    /// The width the Price column is declared at, in logical units: the widest price the column can print,
    /// plus one cell's padding each side.
    /// </summary>
    /// <remarks>
    /// Logical, not pixels: <see cref="TextWidth"/> measures at <paramref name="scale"/> and
    /// <c>Gw.TableColumn</c> multiplies a fixed width by <c>Frame.Scale</c> itself (the same unit rule as
    /// every other <c>Gw.*</c> size). The padding is Glasswork's own - the table pushes
    /// <c>CellPadding = SpaceMedium</c> - so the number is the match for its cells and not a guess.
    /// </remarks>
    /// <param name="look">The Look whose type and spacing the cells are drawn with.</param>
    /// <param name="scale">The UI scale the text is measured at.</param>
    private static float PriceColumnWidth(Look look, float scale) =>
        (TextWidth(MountTable.PriceText(MountTable.WidestPrice), TextRole.Footnote, scale) / scale)
        + (look.SpaceMedium * 2f);

    /// <summary>
    /// True on a frame the header row was pressed: the pointer's button came up over the table, or the key
    /// an activated button answers to. This is what tells a header click from a report that only says the
    /// spec was written.
    /// </summary>
    private static bool HeaderPressed() =>
        ImGui.IsWindowHovered()
        && (ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            || ImGui.IsKeyPressed(ImGuiKey.Space)
            || ImGui.IsKeyPressed(ImGuiKey.GamepadFaceDown));

    /// <summary>
    /// Points the caret at the column <paramref name="order"/> is drawn in, in the direction it sorts.
    /// </summary>
    /// <remarks>
    /// The table has no other way to know the direction: a column declared as the default sort carries none,
    /// and ImGui prefers the spec it saved for the id over such a declaration anyway, so the direction has to
    /// be stated. This is the write that makes the header and the rows agree - it happens before the header
    /// row is submitted - and it marks the table's settings dirty, which is why the caller compares the pair
    /// it read last frame first: an unconditional call would rewrite the ini every frame.
    /// </remarks>
    /// <param name="order">The order the rows are in.</param>
    private static void SetCaret(MountOrder order) =>
        ImGuiP.TableSetColumnSortDirection(
            MountTable.ColumnFor(order),
            MountTable.Descends(order) ? ImGuiSortDirection.Descending : ImGuiSortDirection.Ascending,
            false);

    /// <summary>
    /// The Owners cell: the share of collectors who have this mount, with a filled mark painted behind the
    /// rarest ones so that rarity reads as a shape and not only as a digit to be compared across rows.
    /// </summary>
    /// <remarks>
    /// The mark is a background, not a widget. It is painted into the window's draw list before the run
    /// is drawn - the draw list keeps call order - and it is sized from the run's own width and clamped to
    /// the cell, so it adds nothing to what a fit-content column measures. <c>Gw.Badge</c>'s capsule did,
    /// and its box was not its text: the digits plus one small space of padding each side, and never
    /// narrower than its own height. So the widest item in the column was a decoration rather than a
    /// share, the capsule's edge stood further out than the number of every plain row (38 px around 27 px
    /// of digits in the client), and the column gave up being numeric to hold it - which is why its
    /// percentages lined up at neither edge. The number here is drawn by <c>Gw.TableText</c> in the same
    /// footnote run as the plain cells, so it right-aligns with them to the pixel and only its ink
    /// changes, to whichever ink reads on the fill.
    ///
    /// The mark is the tier's, not the row's - <see cref="MountRarity.IsRare"/> decides, on the rounded
    /// share, so the mark can never contradict the number it sits on - and the tooltip repeats the share
    /// in words for a reader who wants the sentence.
    /// </remarks>
    /// <param name="item">The mount whose row is being drawn.</param>
    /// <param name="look">The Look, for the one colour a mark may use.</param>
    private static void DrawRarity(MountShelfItem item, Look look)
    {
        string text = RarityLabel(item);

        if (text.Length == 0 || !MountRarity.IsRare(item.Community?.OwnedPercent))
        {
            Gw.TableText(text, TextRole.Footnote);
            return;
        }

        Rect mark = RarityMark(text, look);

        // Chosen for the fill and not for the row: the rule Gw.Badge applies to its own label, repeated
        // because the fill is painted by hand here. Its threshold is Glasswork's (0.55 luminance, light
        // fill takes the dark ink), so the mark reads exactly like the badges in the detail pane.
        Color ink = look.Warning.Luminance > 0.55f ? look.InkOverLight : look.InkOverDark;

        ImGui.GetWindowDrawList().AddRectFilled(
            mark.Min,
            mark.Max,
            look.Warning.ToAbgr(),
            MathF.Min(mark.Height * 0.5f, look.RadiusSmall * Frame.Scale));

        Gw.TableText(text, TextRole.Footnote, ink);

        // Gw.TableText reserves the run at the cell's leading edge, so the last item is that reserve and
        // not the mark: the hover is tested against the mark's own rect, with the window-ownership half
        // every hand-rolled hit test here uses.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(mark.Min, mark.Max))
        {
            Gw.TooltipFor(true, MountRarity.Describe(item.Community?.OwnedPercent));
        }
    }

    /// <summary>
    /// The rect a rare share's mark fills, in screen pixels: the run's width, one small space of gutter
    /// each side, centred on the row, ending one gutter past the number.
    /// </summary>
    /// <remarks>
    /// The number's right edge is the cell's content edge (that is what a numeric column aligns to), and
    /// the gutter past it is the cell's own padding - Glasswork pads a cell by one medium space each side
    /// - clamped to that padding so the mark can never reach the next column however it is themed. The
    /// left edge is clamped to the cell for the mirror reason: a share wide enough to fill the column must
    /// not reach back into Owned.
    /// </remarks>
    /// <param name="text">The share as the plain cells print it.</param>
    /// <param name="look">The Look whose type and spacing the cell is drawn with.</param>
    private static Rect RarityMark(string text, Look look)
    {
        var scale = Frame.Scale;
        Vector2 origin = ImGui.GetCursorScreenPos();
        float right = origin.X + MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        float run = TextWidth(text, TextRole.Footnote, scale);
        float gutter = look.SpaceExtraSmall * scale;
        float height = TextPaint.LineHeight(TextRole.Footnote, in look, scale) + (gutter * 2f);

        // Every cell in this column reserves the row's full height, so the cell's cursor is the row's top
        // (the centring offset is (row - content) / 2 and the two are the same height).
        float top = origin.Y + (((look.RowHeight * scale) - height) * 0.5f);
        float end = right + MathF.Min(gutter, look.SpaceMedium * scale);
        float start = MathF.Max(origin.X, end - run - (gutter * 2f));

        return new Rect(new Vector2(start, top), new Vector2(end, top + height));
    }

    /// <summary>
    /// The collected column's whole content: one filled accent dot for a mount the player has, an empty
    /// cell otherwise.
    /// </summary>
    /// <remarks>
    /// The word "Yes" said the same thing in 20 px of text, and the heading alone made the column 112 px
    /// wide - the width the name and the source were short of. The reserve is the row's full height,
    /// exactly as <c>Gw.TableText</c> reserves it, because <c>Gw.TableCell</c> centres a cell by the
    /// height that column's content took the frame before: a shorter reserve would place the dot 5 px low
    /// on the first frame it draws, and the previews render one frame, so the golden would show a
    /// misalignment the client corrects and the reader never sees.
    /// </remarks>
    /// <param name="owned">Whether the player has collected the mount.</param>
    /// <param name="accent">The Look's accent, the same ink the wanted names use.</param>
    private static void DrawOwnedDot(bool owned, Color accent)
    {
        var scale = Frame.Scale;
        var box = OwnedDotWidth * scale;
        // The Look's own token, not MountLook.RowHeight: they differ while FollowGameTheme has the host
        // building its Look from the game's theme, and the reserve has to match the row the table
        // actually draws or the dot sits low inside it.
        var rowHeight = Frame.Look.RowHeight * scale;
        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new Vector2(box, rowHeight));

        if (!owned)
        {
            return;
        }

        ImGui.GetWindowDrawList().AddCircleFilled(
            new Vector2(origin.X + (box * 0.5f), origin.Y + (rowHeight * 0.5f)),
            OwnedDotRadius * scale,
            accent.ToAbgr());
    }

    /// <summary>
    /// The table's seats cell: formatted once per distinct seat count, because the table has no clipper and
    /// so formats every row of the filtered list on every frame.
    /// </summary>
    /// <param name="item">The mount whose row is being drawn.</param>
    private static string SeatsLabel(MountShelfItem item)
    {
        if (TableSeatsLabels.TryGetValue(item.Seats, out string? label))
        {
            return label;
        }

        label = MountTable.SeatsText(item);
        Remember(TableSeatsLabels, item.Seats, label);
        return label;
    }

    /// <summary>
    /// The table's category cell, formatted once per distinct dataset category (28 of them) rather than once
    /// per row: <see cref="UiText.Source(string)"/> folds a spelling through a parsed type, and the fold is the
    /// expensive half.
    /// </summary>
    /// <param name="item">The mount whose row is being drawn.</param>
    private static string SourceLabel(MountShelfItem item)
    {
        string category = MountTable.SourceText(item);

        if (TableSourceLabels.TryGetValue(category, out string? label))
        {
            return label;
        }

        label = UiText.Source(category);
        Remember(TableSourceLabels, category, label);
        return label;
    }

    /// <summary>
    /// The table's price cell, formatted once per distinct price. Keyed by the price and not by the mount:
    /// prices are the one cell that moves between frames, and the key is what keeps a stale number off the row.
    /// </summary>
    /// <param name="price">The cheapest price in gil, or 0 when there is none.</param>
    private static string PriceLabel(int price)
    {
        if (TablePriceLabels.TryGetValue(price, out string? label))
        {
            return label;
        }

        label = MountTable.PriceText(price);
        Remember(TablePriceLabels, price, label);
        return label;
    }

    /// <summary>
    /// The table's rarity cell, formatted once per distinct share. <see cref="double.NaN"/> stands for
    /// "unrecorded" in the key, because a <c>double?</c> cannot be a dictionary key and NaN is the one value the
    /// dataset never carries.
    /// </summary>
    /// <param name="item">The mount whose row is being drawn.</param>
    private static string RarityLabel(MountShelfItem item)
    {
        double key = item.Community?.OwnedPercent ?? double.NaN;

        if (TableRarityLabels.TryGetValue(key, out string? label))
        {
            return label;
        }

        label = MountTable.RarityText(item);
        Remember(TableRarityLabels, key, label);
        return label;
    }

    /// <summary>
    /// Formatted cells, each keyed by the value it is built from rather than by mount id: a catalog refresh
    /// that changes a patch, a seat count or a share must not be served the previous string, and keying on the
    /// value makes that impossible without a revision counter. Bounded by <see cref="MeasureCacheLimit"/>.
    /// </summary>
    private static readonly Dictionary<int, string> TableSeatsLabels = [];
    private static readonly Dictionary<string, string> TableSourceLabels = [];
    private static readonly Dictionary<int, string> TablePriceLabels = [];
    private static readonly Dictionary<double, string> TableRarityLabels = [];

    /// <summary>The width the dot's cell reserves, logical units: the dot plus the gutter beside it.</summary>
    private const float OwnedDotWidth = 12f;
    /// <summary>The dot's radius, logical units.</summary>
    private const float OwnedDotRadius = 4f;
}
