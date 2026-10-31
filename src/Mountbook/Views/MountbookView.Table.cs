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
        Gw.TableColumn("Name"u8, 3f, true, MountTable.ColumnFor(state.Order) == MountTable.NameColumn, false);
        Gw.TableColumn("Patch"u8, 0f, false, MountTable.ColumnFor(state.Order) == MountTable.PatchColumn, false);
        Gw.TableColumn("Seats"u8, 0f, false, MountTable.ColumnFor(state.Order) == MountTable.SeatsColumn, true);
        Gw.TableColumn("Source"u8, 2f, true, false, false);
        Gw.TableColumn("Collected"u8, 0f, false, MountTable.ColumnFor(state.Order) == MountTable.OwnedColumn, false);
        Gw.TableColumn("Owned"u8, 0f, false, MountTable.ColumnFor(state.Order) == MountTable.RarityColumn, true);
        Gw.TableColumn("Price"u8, 0f, false, MountTable.ColumnFor(state.Order) == MountTable.PriceColumn, false);

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
            Gw.TableText(MountTable.SourceText(item), TextRole.Footnote);

            Gw.TableCell();
            Gw.TableText(MountTable.OwnedText(item), TextRole.Footnote);

            Gw.TableCell();
            Gw.TableText(MountTable.RarityText(item), TextRole.Footnote);

            // Empty until the prices are read: a column of dashes would read as "free", and the row
            // order already parks the unpriced mounts at the end when this is the sort column.
            Gw.TableCell();
            Gw.TableText(
                MountTable.PriceText(state.Prices.TryGetValue(item.Id, out int price) ? price : 0),
                TextRole.Footnote);
        }

        Gw.EndTable();
    }
}
