using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The header caret against the click sequences that used to leave it behind: this drives the production
/// frame rule (<see cref="TableSort.Observe"/>, the one call <c>MountbookView.DrawTable</c> makes) through a
/// stand-in for ImGui's sortable table, and asserts both halves of what a click has to leave behind - the
/// order the window persists, and the column the header draws its caret on.
/// </summary>
/// <remarks>
/// <para>
/// The stand-in is not gold-plating: the bug lives in the difference between the two things ImGui holds.
/// A sort spec belongs to a table <em>id</em> and outlives the frames that id is not drawn; a click is
/// applied to the table being drawn that frame - which is the id of the order being <em>left</em>, because
/// every order has its own id - and ImGui reports a dirty spec only on the frame it changed or on the frame
/// a table is first drawn, never on the frames a table drawn earlier becomes current again. So "Owners,
/// then Name" ends with the rows in name order and the caret still on Owners, which no single-frame
/// assertion can see. <see cref="Table"/> models exactly those three rules and nothing else.
/// </para>
/// <para>
/// It is a unit test rather than a golden on purpose, and the measurement is the reason: a preview scene
/// that scripts the same two clicks does reproduce the bug - the caret ends up on Owners over name-ordered
/// rows - but the caret is about 48 pixels of a 1088x708 image, and <c>--compare</c> passes a scene while
/// 0.1% of its pixels differ. A defect that only moves a caret is 0.006% of the picture, so a golden stays
/// green through it. The caret has to be asserted on the state the header is drawn from, next to the order
/// the window persists.
/// </para>
/// <para>
/// The pairs are the ones the fix was reported against, plus the same-header re-click: Owners and Owned are
/// the two columns whose caret can point two ways, and the direction has to survive the return as well.
/// </para>
/// </remarks>
public sealed class TableSortTests
{
    [Fact]
    public void OwnersThenNameLeavesBothTheOrderAndTheCaretOnName()
    {
        var table = new Table(MountOrder.Name);

        table.Frame();
        table.Frame(MountTable.RarityColumn);
        table.Frame();
        table.Frame(MountTable.NameColumn);
        table.Settle();

        // The config readback: MountOrder.Name is the value the window writes to disk, and 0 is what an
        // older settings file would still hold for it.
        Assert.Equal(MountOrder.Name, table.Order);
        Assert.Equal(0, (int)table.Order);

        // And the column the header's caret is drawn on, which is what the report is about.
        Assert.Equal(MountTable.NameColumn, table.Caret.Column);
        Assert.False(table.Caret.Descending);
    }

    [Fact]
    public void OwnedThenNameLeavesBothTheOrderAndTheCaretOnName()
    {
        var table = new Table(MountOrder.Name);

        table.Frame();
        table.Frame(MountTable.OwnedColumn);
        table.Frame();
        table.Frame(MountTable.NameColumn);
        table.Settle();

        Assert.Equal(MountOrder.Name, table.Order);
        Assert.Equal(0, (int)table.Order);
        Assert.Equal(MountTable.NameColumn, table.Caret.Column);
        Assert.False(table.Caret.Descending);
    }

    [Fact]
    public void ClickingTheSortedHeadingTwiceLeavesTheCaretOnItPointingDown()
    {
        var table = new Table(MountOrder.Name);

        table.Frame();
        table.Frame(MountTable.RarityColumn);
        table.Frame();
        table.Frame(MountTable.RarityColumn);
        table.Settle();

        // Rarest first, then Commonest first: the second click is the same column, so ImGui flips the
        // direction rather than asking for a different order - and the caret has to follow that too, not
        // only the column.
        Assert.Equal(MountOrder.CommonestFirst, table.Order);
        Assert.Equal(MountTable.RarityColumn, table.Caret.Column);
        Assert.True(table.Caret.Descending);
    }

    [Fact]
    public void AHeaderWithNoOrderOfItsOwnLeavesTheOrderAndSnapsTheCaretBack()
    {
        var table = new Table(MountOrder.Name);

        table.Frame();
        table.Frame(MountTable.SourceColumn);
        table.Settle();

        // "Source first" is not an order the list has, so the press asks for nothing; the caret still has
        // to say what the rows say.
        Assert.Equal(MountOrder.Name, table.Order);
        Assert.Equal(MountTable.NameColumn, table.Caret.Column);
    }

    /// <summary>
    /// One session of a sortable table, as far as the caret is concerned: a saved sort spec per table id, a
    /// header click that writes the spec of the table being drawn and toggles its direction when the column
    /// is already sorted, and a report that fires only while the spec is dirty.
    /// </summary>
    private sealed class Table(MountOrder order)
    {
        /// <summary>What ImGui restores when an id is drawn again: the spec it saved for that id.</summary>
        private readonly Dictionary<MountOrder, (int Column, bool Descending)> saved = [];

        /// <summary>The ids this session has already drawn, which is what decides the first frame of one.</summary>
        private readonly HashSet<MountOrder> entered = [];

        /// <summary>The sort spec of the open table: what its header draws and what a click writes.</summary>
        private (int Column, bool Descending) spec = (MountTable.ColumnFor(order), false);

        /// <summary>Whether the spec changed since the last report.</summary>
        private bool dirty;

        /// <summary>The order the rows are in: the window's config readback.</summary>
        public MountOrder Order { get; private set; } = order;

        /// <summary>The column and direction the header drew its caret with, on the last frame.</summary>
        public (int Column, bool Descending) Caret { get; private set; } = (MountTable.ColumnFor(order), false);

        public void Frame(int? clicked = null)
        {
            // The id is chosen from the order before the click is applied: DrawTable reads it at the top
            // of the frame, and the click's spec write lands on that open table.
            MountOrder id = Order;

            // ImGui applies the settings it saved for an id every time that id is drawn: the spec comes
            // back with the rest, which is why the returning table is still sorted by the column that was
            // clicked to leave it. Only the session's first frame of an id reports the spec as a change.
            spec = saved.TryGetValue(id, out var restored) ? restored : (MountTable.ColumnFor(id), false);

            if (entered.Add(id))
            {
                // A table's first frame: its saved spec, or the column the table declares as its default
                // sort (ascending - a declaration carries no direction).
                dirty = true;
            }

            if (clicked is int column && MountTable.OrderFor(column, ascending: true) is not null)
            {
                spec = (column, spec.Column == column ? !spec.Descending : false);
                dirty = true;
            }

            (int Column, bool Descending) drawn = spec;
            bool reported = dirty;
            dirty = false;

            TableSort sort = TableSort.Observe(Order, drawn.Column, drawn.Descending, reported, clicked is not null);

            // SetCaret writes the open table's spec, and a write marks the settings dirty: the repair is
            // not visible until the next frame, because ImGui has already laid this frame's header out.
            if (sort.Column != drawn.Column || sort.Descending != drawn.Descending)
            {
                spec = (sort.Column, sort.Descending);
                dirty = true;
            }

            saved[id] = spec;
            Caret = drawn;
            Order = sort.Order;
        }

        /// <summary>Idle frames, which is where a stale caret used to sit.</summary>
        public void Settle()
        {
            for (var frame = 0; frame < 4; frame++)
            {
                Frame();
            }
        }
    }
}
