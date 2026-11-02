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
/// A sort spec belongs to a table id and is restored, unchanged, on every frame that id is drawn; a write
/// makes it dirty for exactly one frame; and a click is applied to the spec <em>during</em> the header row,
/// after the frame that wrote it, in a direction ImGui chooses. So a click on Seats asks for rows sorted
/// most-seats-first while the spec it leaves says ascending - the caret points up over rows that run down -
/// and only the frame after it can repair that. <see cref="Table"/> models those rules and the view's two
/// writes, and nothing else.
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

    [Fact]
    public void ASpecSavedInAnEarlierSessionNeverReachesTheHeader()
    {
        // One id means the spec ImGui loads from its ini is this table's own: the column the plugin was last
        // closed on. The first frame writes over it before the header draws, so the caret opens on the column
        // that shows the rows' order - which is the bug 83f8277 fixed, now with no second table involved.
        var table = new Table(MountOrder.Name, (MountTable.OwnedColumn, true));

        table.Frame();

        Assert.Equal(MountOrder.Name, table.Order);
        Assert.Equal(MountTable.NameColumn, table.Caret.Column);
        Assert.False(table.Caret.Descending);
    }

    [Fact]
    public void ASteadyFrameWritesNothing()
    {
        // The write marks the table's settings dirty, and ImGui saves them when they are: a write every
        // frame would rewrite the ini sixty times a second. So the view compares the pair it read last frame
        // and writes only when the header disagrees with the rows.
        var table = new Table(MountOrder.Name);

        table.Frame();
        table.Frame();

        Assert.False(table.Reported);

        // And it still writes the moment the order changes, with or without a click behind it. The click
        // frame itself still draws the direction ImGui gave it (ascending, over rows that run down) - that
        // frame's repair is what the next one draws, which is the one a player can see.
        table.Frame(MountTable.SeatsColumn);

        Assert.True(table.Reported);
        Assert.Equal(MountOrder.Seats, table.Order);

        table.Settle();

        Assert.Equal(MountTable.SeatsColumn, table.Caret.Column);
        Assert.True(table.Caret.Descending);
    }

    /// <summary>
    /// One session of the sortable table, as far as the caret is concerned: one saved spec (ImGui keeps it
    /// for the one id, across sessions), a header click that writes it in ImGui's own direction, a report
    /// that fires only while it is dirty, and the view's own writes - the order's spec before the header row
    /// and the repair after it.
    /// </summary>
    private sealed class Table(MountOrder order, (int Column, bool Descending)? fromAnEarlierSession = null)
    {
        /// <summary>What ImGui restores on every frame the id is drawn: the spec it saved last time.</summary>
        private (int Column, bool Descending) saved = fromAnEarlierSession ?? (MountTable.ColumnFor(order), false);

        /// <summary>The spec the view read back on the last frame, or null before it has read one.</summary>
        private (int Column, bool Descending)? read;

        /// <summary>Whether the spec changed since the last report.</summary>
        private bool dirty;

        /// <summary>The order the rows are in: the window's config readback.</summary>
        public MountOrder Order { get; private set; } = order;

        /// <summary>The column and direction the header drew its caret with, on the last frame.</summary>
        public (int Column, bool Descending) Caret { get; private set; } = (MountTable.ColumnFor(order), false);

        /// <summary>Whether the last frame reported the spec as dirty, which is what the table saves.</summary>
        public bool Reported { get; private set; }

        public void Frame(int? clicked = null)
        {
            (int Column, bool Descending) spec = saved;

            // The view's write, before the header row: the order's spec whenever the pair it read last
            // frame says something else. The first frame has read nothing, so it writes - which is how a
            // spec ImGui restored from a previous session (a "Newest first" list opened as "Owned first")
            // is corrected before anything draws it.
            TableSort wanted = TableSort.At(Order);

            if (read is not { } live || live.Column != wanted.Column || live.Descending != wanted.Descending)
            {
                spec = (wanted.Column, wanted.Descending);
                dirty = true;
            }

            // The click, during the header row: ImGui writes the clicked column, ascending unless it is the
            // column already sorted, which it flips. It does that for every sortable column, including the
            // ones the list has no order for.
            if (clicked is int column)
            {
                spec = (column, spec.Column == column ? !spec.Descending : false);
                dirty = true;
            }

            // The header's ink: the caret is drawn from the spec the click left behind.
            Caret = spec;

            bool reported = dirty;
            Reported = reported;
            dirty = false;
            read = spec;

            TableSort sort = TableSort.Observe(Order, spec.Column, spec.Descending, reported, clicked is not null);
            Order = sort.Order;

            // The repair after the header, which the next frame draws: the write that turns a click's
            // ascending spec into the direction its order actually sorts (or back off a header that has no
            // order at all).
            saved = sort.Column != spec.Column || sort.Descending != spec.Descending
                ? (sort.Column, sort.Descending)
                : spec;

            if (sort.Column != spec.Column || sort.Descending != spec.Descending)
            {
                dirty = true;
            }
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
