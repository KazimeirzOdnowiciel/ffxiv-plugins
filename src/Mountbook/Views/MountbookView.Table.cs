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
/// The rows are drawn inside the scroll child the grid uses, in the order the caller produced, so the
/// table never sorts anything itself: a header click is translated into a <see cref="MountOrder"/> by
/// <see cref="MountTable.OrderFor"/> and the window re-queries. Two orderings would eventually disagree
/// about what "oldest first" means, which is how the patch parser ended up duplicated.
/// </remarks>
public sealed partial class MountbookView
{
    private static void DrawTable(MountViewState state)
    {
        const GwTableFlags Flags =
            GwTableFlags.Sortable | GwTableFlags.Resizable | GwTableFlags.Zebra | GwTableFlags.Selectable;

        // The pane's width, in logical units (the table scales its own size). A zero size left the table
        // as wide as its content - 675 px of a 1162 px pane - which the client showed as a table that
        // stopped two thirds of the way across.
        var width = ImGui.GetContentRegionAvail().X / Frame.Scale;

        // The table id carries the order, so a different order is a different ImGui table. ImGui remembers
        // a table's sort spec and its column widths by id - across sessions - so a shared id left the caret
        // (and the widths) of whichever order was drawn last. A fresh id per order keeps each order's table
        // to itself; what the id still restores is overruled below, every frame. Surprise has no id of its
        // own on purpose: it draws the name column's caret, so it shares the name order's table.
        ReadOnlySpan<byte> tableId = state.Order switch
        {
            MountOrder.OldestFirst => "##mountsTable1"u8,
            MountOrder.Seats => "##mountsTable2"u8,
            MountOrder.OwnedFirst => "##mountsTable3"u8,
            MountOrder.NewestFirst => "##mountsTable4"u8,
            MountOrder.RarestFirst => "##mountsTable5"u8,
            MountOrder.Cheapest => "##mountsTable6"u8,
            MountOrder.CommonestFirst => "##mountsTable7"u8,
            _ => "##mountsTable0"u8,
        };

        if (!Gw.BeginTable(tableId, MountTable.ColumnCount, Flags, new Vector2(width, 0f)))
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
        Gw.TableColumn(Loc.T("table.name"), 3f, true, MountTable.ColumnFor(state.Order) == MountTable.NameColumn, false);
        Gw.TableColumn(Loc.T("table.patch"), 0f, false, MountTable.ColumnFor(state.Order) == MountTable.PatchColumn, false);
        Gw.TableColumn(Loc.T("table.seats"), 0f, false, MountTable.ColumnFor(state.Order) == MountTable.SeatsColumn, true);
        Gw.TableColumn(Loc.T("table.source"), 2f, true, false, false);

        // "Owned" is the player's own bit, drawn as one dot instead of the word "Yes"; "Owners" is the
        // share of collectors who have the mount, which the column had long called "Owned" while showing
        // a percentage nobody owned. The heading is what a fit-content column is sized by, so the dot's
        // column is as narrow as that five-letter word allows: 112 px of "Collected" was most of the
        // width the name and the source were short of.
        //
        // Owners is not a numeric column any more: a rare share is drawn as a capsule badge, and a badge
        // cannot be right-aligned inside a numeric column without a width the facade does not report, so
        // the header, the badges and the plain percentages all share the cell's left edge instead.
        Gw.TableColumn(Loc.T("table.owned"), 0f, false, MountTable.ColumnFor(state.Order) == MountTable.OwnedColumn, false);
        Gw.TableColumn(Loc.T("table.owners"), 0f, false, MountTable.ColumnFor(state.Order) == MountTable.RarityColumn, false);
        Gw.TableColumn(Loc.T("table.price"), 0f, false, MountTable.ColumnFor(state.Order) == MountTable.PriceColumn, false);

        Gw.TableHeaders();

        // Only a pressed header is a click. Glasswork reports a sort change both for a click and for the
        // spec ImGui restores for this id - preferred over the column declared as the default once one
        // exists - so believing every report replaced the player's saved order with whatever column that id
        // was last left on (a persisted "Newest first" opened as "Owned first"), and one click could
        // cascade: every order is its own id, and each one restored a spec of its own.
        //
        // The report is read for its column and direction every frame, not only on the frame it returns
        // true: Gw.TableSortChanged fills those two from the live spec either way, and the report itself
        // only fires while the spec is dirty - on a click, on a table's first frame, or after ImGui drops
        // its transient buffers. It does not fire when a table drawn earlier in the session becomes
        // current again, which is exactly the return that used to keep a stale caret: a header click is
        // applied by ImGui to the table that is being drawn, i.e. to the id of the order being left, so
        // coming back to that order re-enters a table still sorted by the column that was clicked to leave
        // it, while the rows are already in the new order.
        bool changed = Gw.TableSortChanged(out int column, out bool descending);

        // One call decides the frame: the click's order, and the caret that order belongs under. It is a
        // pure function in Services/ because the rule is about the table's frames - a report that does not
        // fire on the return - and not about ImGui, so the click sequence that used to leave the caret
        // behind is pinned by TableSortTests without a renderer.
        TableSort sort = TableSort.Observe(state.Order, column, descending, changed, HeaderPressed());

        if (sort.Order != state.Order)
        {
            state.Order = sort.Order;
        }

        // The caret follows the rows, which are the single source of truth: the column that shows the
        // order, in the direction it sorts. It is written whenever the table's spec says something else - a spec
        // ImGui restored (including the one it loads from the ini at startup), or the spec the click just
        // wrote to the id being left - because a declared default sort cannot do it alone: it carries no
        // direction, so a descending order drew an ascending caret over rows sorted the other way. The
        // comparison is what keeps the write to the frames the spec actually lies; on the returning frame
        // ImGui has already computed the header, so the caret is right on the next one.
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
            // order already parks the unpriced mounts at the end when this is the sort column.
            Gw.TableCell();
            Gw.TableText(
                PriceLabel(state.Prices.TryGetValue(item.Id, out int price) ? price : 0),
                TextRole.Footnote);
        }

        Gw.EndTable();
    }

    /// <summary>
    /// True on a frame the header row was pressed: the pointer's button came up over the table, or the key
    /// an activated button answers to. This is what tells a header click from a sort spec ImGui restored.
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
    /// The table has no other way to know the direction: <c>Gw.TableColumn</c> declares a column as the
    /// default sort without one, and ImGui prefers that declaration only while it has no saved spec for the
    /// id, so the direction has to be stated. This marks the table's settings dirty, which is why the caller
    /// asks <see cref="MountTable.DescribedBy"/> first - an unconditional call would rewrite the ini every
    /// frame.
    /// </remarks>
    /// <param name="order">The order the rows are in.</param>
    private static void SetCaret(MountOrder order) =>
        ImGuiP.TableSetColumnSortDirection(
            MountTable.ColumnFor(order),
            MountTable.Descends(order) ? ImGuiSortDirection.Descending : ImGuiSortDirection.Ascending,
            false);

    /// <summary>
    /// The Owners cell: the share of collectors who have this mount, drawn as a badge for the rarest ones
    /// so that rarity reads as a mark and not only as a digit to be compared across rows.
    /// </summary>
    /// <remarks>
    /// The badge carries the same percentage the plain cell would, so it cannot widen the column past the
    /// "Owners" heading that already sizes it. The mark is the tier's, not the row's -
    /// <see cref="MountRarity.IsRare"/> decides, on the rounded share, so the mark can never contradict the
    /// number it sits on - and the tooltip repeats the share in words for a reader who wants the sentence.
    /// </remarks>
    /// <param name="item">The mount whose row is being drawn.</param>
    /// <param name="look">The Look, for the one colour a badge may use.</param>
    private static void DrawRarity(MountShelfItem item, Look look)
    {
        string text = RarityLabel(item);

        if (text.Length == 0 || !MountRarity.IsRare(item.Community?.OwnedPercent))
        {
            Gw.TableText(text, TextRole.Footnote);
            return;
        }

        Gw.Badge(text, look.Warning);

        // Built only while hovered: the sentence goes through the string table, and it used to be formatted for
        // every rare row of the list on every frame, hovered or not.
        if (ImGui.IsItemHovered())
        {
            Gw.TooltipFor(true, MountRarity.Describe(item.Community?.OwnedPercent));
        }
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
