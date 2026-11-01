namespace Mountbook.Services;

/// <summary>
/// The table header's sort as one frame of it reads and draws: the order the rows are in, the column the
/// caret is on, and whether it points down. The view settles one of these per frame by calling
/// <see cref="Observe"/>, so the frame-by-frame rule - which column a click asks for, and what the caret
/// shows when the table reports something else - is Dalamud-free and tested without ImGui (T1).
/// </summary>
/// <remarks>
/// <para>
/// The caret is not Mountbook's to draw: Glasswork's header draws it from ImGui's per-table sort spec, and
/// the only way to read that spec is <c>Gw.TableSortChanged</c>'s two outs - filled from the live spec on
/// every frame, whether or not it reports a change. <see cref="Observe"/> therefore takes the live spec and
/// answers what the header shows, which is the pair the view has to compare against the order.
/// </para>
/// <para>
/// The reason that comparison exists at all: a header click is applied by ImGui to the table being drawn
/// that frame, i.e. to the id of the order being left (the view gives every order its own table id). Coming
/// back to that order opens a table still sorted by the column that was clicked to leave it, and ImGui
/// reports that restored spec only on the table's first frame - not on every frame it is current - so
/// "apply the report when it fires" left the caret on the column the player had clicked away from while the
/// rows were already in the order they asked for.
/// </para>
/// </remarks>
/// <param name="Order">The order the rows are drawn in, which is what the window persists.</param>
/// <param name="Column">The column the header draws its caret on this frame.</param>
/// <param name="Descending">Whether that caret points down.</param>
public readonly record struct TableSort(MountOrder Order, int Column, bool Descending)
{
    /// <summary>
    /// The sort <paramref name="order"/> is drawn cleanly as: its own column
    /// (<see cref="MountTable.ColumnFor"/>) and the direction it sorts (<see cref="MountTable.Descends"/>).
    /// This is both the default the table declares and what a repaired caret is set to.
    /// </summary>
    /// <param name="order">The order the rows are in.</param>
    public static TableSort At(MountOrder order) =>
        new(order, MountTable.ColumnFor(order), MountTable.Descends(order));

    /// <summary>
    /// One frame of the header: a click changes the order, and the caret is read from the column
    /// <paramref name="order"/> belongs in when the table reports a spec that does not describe it.
    /// </summary>
    /// <remarks>
    /// <paramref name="reported"/> only says whether the spec was dirty this frame - ImGui reports a click,
    /// a table's first frame and the buffer it drops, but not a table that becomes current again - so the
    /// caret is settled against the live spec on every frame instead of inside that branch. A report is
    /// still only believed for an order when <paramref name="pressed"/> says a header was actually clicked,
    /// because a restored spec is not a click (<see cref="MountTable.ClickedOrder"/>).
    /// </remarks>
    /// <param name="order">The order the rows are in, before this frame's click.</param>
    /// <param name="column">The column the table reports as sorted, as <c>Gw.TableSortChanged</c> fills it.</param>
    /// <param name="descending">Whether the table reports it descending.</param>
    /// <param name="reported">Whether the table reported a change this frame; the two outs above are the
    /// live spec either way.</param>
    /// <param name="pressed">Whether a header was pressed this frame, which is what makes a report a click.</param>
    public static TableSort Observe(MountOrder order, int column, bool descending, bool reported, bool pressed) =>
        Shown((reported ? MountTable.ClickedOrder(column, descending, pressed) : null) ?? order, column, descending);

    /// <summary>
    /// The caret <paramref name="order"/> is drawn with: the spec when it already describes the order, else
    /// the order's own column and direction - which is what <see cref="MountbookView"/> then writes with
    /// <c>TableSetColumnSortDirection</c>, so the caret is right from the next frame.
    /// </summary>
    private static TableSort Shown(MountOrder order, int column, bool descending) =>
        MountTable.DescribedBy(order, column, descending) ? new(order, column, descending) : At(order);
}
