using System;
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

        // The table id carries the order, so a different order is a different ImGui table. ImGui
        // remembers a table's sort spec by id - across sessions - and restores it on the first draw, so a
        // shared id left the caret on "Patch" while the rows were ordered by price. A fresh id per order
        // makes the declared default sort the one the header shows, and a header click still works: it
        // changes the order, and so the id.
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

        // Declaring the current order as the default sort is not enough on its own: the table also
        // restores the spec ImGui remembered for this id, so the first frame after it appears can report
        // a change nobody made. That frame is skipped; every later change is a header click.
        if (Gw.TableSortChanged(out int column, out bool ascending)
            && state.TableSortReady
            && MountTable.OrderFor(column, ascending) is { } order)
        {
            state.Order = order;
        }

        state.TableSortReady = true;

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
            Gw.TableText(MountTable.SeatsText(item), TextRole.Footnote);

            Gw.TableCell();
            Gw.TableText(UiText.Source(MountTable.SourceText(item)), TextRole.Footnote);

            Gw.TableCell();
            DrawOwnedDot(item.Owned, accent);

            Gw.TableCell();
            DrawRarity(item, Frame.Look);

            // Empty until the prices are read: a column of dashes would read as "free", and the row
            // order already parks the unpriced mounts at the end when this is the sort column.
            Gw.TableCell();
            Gw.TableText(
                MountTable.PriceText(state.Prices.TryGetValue(item.Id, out int price) ? price : 0),
                TextRole.Footnote);
        }

        Gw.EndTable();
    }

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
        string text = MountTable.RarityText(item);

        if (text.Length == 0 || !MountRarity.IsRare(item.Community?.OwnedPercent))
        {
            Gw.TableText(text, TextRole.Footnote);
            return;
        }

        Gw.Badge(text, look.Warning);
        Gw.TooltipFor(ImGui.IsItemHovered(), MountRarity.Describe(item.Community?.OwnedPercent));
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

    /// <summary>The width the dot's cell reserves, logical units: the dot plus the gutter beside it.</summary>
    private const float OwnedDotWidth = 12f;
    /// <summary>The dot's radius, logical units.</summary>
    private const float OwnedDotRadius = 4f;
}
