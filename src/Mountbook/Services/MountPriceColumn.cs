using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// When the window has to have market prices, and which rows have one worth drawing.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1): both halves are decisions, not draws - the window asks the first one every
/// frame, the table asks the second one per cell.
/// </para>
/// <para>
/// The first half is the fix for a column of blanks. A read was offered only while the list was ordered
/// cheapest-first, so a player who never touched that order - or who touched it once and came back later -
/// saw the table's Price column holding whatever that last visit had left in the cache: one price here, a
/// gap, another price there. The Price column is part of the table whatever the order, so the table frame is
/// itself the request; the cheapest-first order stays one too, because the sort key is a price the card grid
/// would otherwise never read.
/// </para>
/// <para>
/// The second half is what keeps a cell honest: the table hands every row through it, so a price that has
/// not been read, one the market answered as "nobody sells this", and one left over from a read that has
/// since been replaced all draw the same empty cell - never a stale number and never a 0 that reads as free.
/// </para>
/// </remarks>
public static class MountPriceColumn
{
    /// <summary>
    /// Whether the window needs prices right now: the dense table draws the Price column, or the list is
    /// ordered by price (a sort key is drawn even when no column shows it).
    /// </summary>
    /// <param name="mode">The view the shelf is showing.</param>
    /// <param name="order">The order the rows are in.</param>
    public static bool Wanted(MountViewMode mode, MountOrder order) =>
        mode == MountViewMode.Table || order == MountOrder.Cheapest;

    /// <summary>
    /// The price a row draws, or 0 for none. 0 is also what a batch answer leaves for "asked, nobody sells
    /// it" - a real answer, but not a price - so the cell stays empty rather than claiming the mount is free.
    /// </summary>
    /// <param name="prices">Cheapest known price per mount id.</param>
    /// <param name="mountId">The mount whose row is being drawn.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prices"/> is null.</exception>
    public static int PriceOf(IReadOnlyDictionary<uint, int> prices, uint mountId)
    {
        ArgumentNullException.ThrowIfNull(prices);

        return prices.TryGetValue(mountId, out int price) && price > 0 ? price : 0;
    }
}
