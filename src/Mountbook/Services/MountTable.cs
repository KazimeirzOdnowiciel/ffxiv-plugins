using System;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>
/// The dense table's cells: the facts the card grid shows as badges, one column each.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1), and deliberately formatting rather than modelling: the row order, the
/// selection and the wanted markers all come from the list and the state the view already draws, so
/// the table cannot disagree with the grid about which mounts there are.
/// </para>
/// <para>
/// Every unrecorded fact is an empty cell, never a placeholder: 13 of the 369 mounts have no obtain
/// data, and a "-" or a "0%" in a column reads as a measurement rather than as missing data.
/// </para>
/// </remarks>
public static class MountTable
{
    /// <summary>The name cell's column index; also the table's default sort.</summary>
    public const int NameColumn = 0;

    /// <summary>Index of the patch column.</summary>
    public const int PatchColumn = 1;

    /// <summary>Index of the seat-count column.</summary>
    public const int SeatsColumn = 2;

    /// <summary>Index of the acquire-method column.</summary>
    public const int SourceColumn = 3;

    /// <summary>Index of the collected column: the dot the view draws for a mount the player has.</summary>
    public const int OwnedColumn = 4;

    /// <summary>Index of the community-ownership column ("Owners": the share of collectors who have it).</summary>
    public const int RarityColumn = 5;

    /// <summary>Index of the market-price column, empty until prices have been read.</summary>
    public const int PriceColumn = 6;

    /// <summary>How many columns the table declares.</summary>
    public const int ColumnCount = 7;

    /// <summary>The patch a mount arrived in, or empty when the dataset does not record one.</summary>
    /// <param name="item">The mount to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string PatchText(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Patch ?? string.Empty;
    }

    /// <summary>The seat count as a bare number, because the column header already says "Seats".</summary>
    /// <param name="item">The mount to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string SeatsText(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Seats.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The first source line's category ("Trial", "Premium", ...), or empty when the mount has no
    /// obtain data. The first line is the primary way to get the mount; the detail pane lists the rest.
    /// </summary>
    /// <param name="item">The mount to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string SourceText(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Sources.Count > 0 ? item.Sources[0].Category : string.Empty;
    }

    /// <summary>
    /// The share of collectors who own the mount, as <c>3.4%</c>, <c>&lt;0.1%</c> for a rounded zero,
    /// or empty when the rarity is unrecorded. A rounding zero is printed as "fewer than one step"
    /// rather than as 0%, which the site never claimed.
    /// </summary>
    /// <param name="item">The mount to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string RarityText(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return MountRarity.ShareText(item.Community?.OwnedPercent);
    }

    /// <summary>
    /// The cheapest known market price as a bare number, because the column header already says
    /// "Price", or empty when the price is unknown (never asked, nobody sells it, or untradeable).
    /// </summary>
    /// <param name="price">The cheapest price in gil, or 0 when there is none.</param>
    public static string PriceText(int price) =>
        price > 0 ? price.ToString("N0", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// The column that shows <paramref name="order"/>, or the name column for an order no column
    /// carries: the column the table declares as its default sort, and the one the caret has to point at.
    /// </summary>
    /// <param name="order">The order the list is currently in.</param>
    public static int ColumnFor(MountOrder order) =>
        order switch
        {
            MountOrder.OldestFirst or MountOrder.NewestFirst => PatchColumn,
            MountOrder.Seats => SeatsColumn,
            MountOrder.OwnedFirst => OwnedColumn,
            MountOrder.RarestFirst or MountOrder.CommonestFirst => RarityColumn,
            MountOrder.Cheapest => PriceColumn,
            _ => NameColumn,
        };

    /// <summary>
    /// Whether <paramref name="order"/> runs its column's key from high to low, which is also the way its caret
    /// has to point: the column is declared as the table's default sort, and a declared default carries no
    /// direction, so a high-first order is drawn ascending until something says otherwise.
    /// </summary>
    /// <remarks>
    /// This is a reading of the comparators in <see cref="MountQuery"/>, not a statement about where an order
    /// sits in a pair. "Seats" and "Owned first" are the only orders their column has and neither is a second
    /// click on anything, but both put the larger key on top: seats sort <c>right.Seats.CompareTo(left.Seats)</c>
    /// (the most seats first, which is what the column is for) and the owned bit sorts true first (an owned
    /// mount outranks an unowned one, which is the order's own name). So both carets point down. Until this
    /// said so, the Seats caret claimed ascending over rows sorted 8, 4, 4, 4.
    /// <para>
    /// Every entry here is bound to the comparator it names by
    /// <c>MountQueryTests.TheCaretPointsTheWayEveryOrdersRowsRun</c>, which sorts a probe shelf with the real
    /// comparators and measures the direction of the result. Asserting this list against itself - which is what
    /// the earlier test did, through <see cref="DescribedBy"/> - is what let the two disagree in the first place.
    /// </para>
    /// </remarks>
    /// <param name="order">The order the rows are in.</param>
    public static bool Descends(MountOrder order) =>
        order is MountOrder.Seats or MountOrder.OwnedFirst or MountOrder.NewestFirst or MountOrder.CommonestFirst;

    /// <summary>
    /// True when a report of <paramref name="column"/> sorted <paramref name="descending"/> is what
    /// <paramref name="order"/> already looks like in the table, so the caret is telling the truth.
    /// </summary>
    /// <param name="order">The order the rows are in.</param>
    /// <param name="column">The column the table reports as sorted.</param>
    /// <param name="descending">Whether the table reports it descending, as <c>Gw.TableSortChanged</c> does.</param>
    public static bool DescribedBy(MountOrder order, int column, bool descending) =>
        ColumnFor(order) == column && Descends(order) == descending;

    /// <summary>
    /// The order a header click asks for, or null for a column that has no order of its own (the
    /// acquire method: "Trial first" is not a question anyone asks).
    /// </summary>
    /// <remarks>
    /// This deliberately maps onto the existing <see cref="MountOrder"/> values instead of sorting the
    /// rows here: one ordering implementation is what keeps the table, the grid and the Order combo from
    /// disagreeing, which is the same reason the patch parser was unified.
    /// <para>
    /// The Owners column is the second one whose arrow means something: ascending is the rarest share
    /// first, descending the largest share first, and until the descending order existed the caret could
    /// show "descending" over rows sorted ascending.
    /// </para>
    /// </remarks>
    /// <param name="column">The column index the table reports as sorted.</param>
    /// <param name="ascending">Whether the table reports it ascending.</param>
    public static MountOrder? OrderFor(int column, bool ascending) =>
        column switch
        {
            NameColumn => MountOrder.Name,
            PatchColumn => ascending ? MountOrder.OldestFirst : MountOrder.NewestFirst,
            SeatsColumn => MountOrder.Seats,
            OwnedColumn => MountOrder.OwnedFirst,
            RarityColumn => ascending ? MountOrder.RarestFirst : MountOrder.CommonestFirst,
            PriceColumn => MountOrder.Cheapest,
            _ => null,
        };

    /// <summary>
    /// The order a sort report asks for, or null when the report is not a header click.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Glasswork reports a sort change for a click and for the sort spec ImGui restores from its own saved
    /// settings as well - the settings are kept per table id, across sessions, and ImGui ignores the column
    /// the table declares as its default once it has any. Since the id is the order, the restored spec is
    /// whatever column that order was last left on: clicking "Owned" while the list was on "Newest first"
    /// saved Owned under that order's id, and the next session opened the list on Owned first instead. A
    /// report is therefore only believed on a frame the header was actually pressed - a click is what the
    /// report is for, and a restored spec is not a click.
    /// </para>
    /// <para>
    /// The direction is <paramref name="descending"/> because that is the one <c>Gw.TableSortChanged</c>
    /// reports: reading it as ascending inverted both directions of the two columns that have two, so the
    /// Patch and Owners carets pointed one way and the rows went the other.
    /// </para>
    /// </remarks>
    /// <param name="column">The column the table reports as sorted.</param>
    /// <param name="descending">Whether the table reports it descending.</param>
    /// <param name="pressed">Whether the header was pressed this frame.</param>
    public static MountOrder? ClickedOrder(int column, bool descending, bool pressed) =>
        pressed ? OrderFor(column, !descending) : null;
}
