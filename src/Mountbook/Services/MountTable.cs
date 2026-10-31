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

    /// <summary>Index of the collected column.</summary>
    public const int OwnedColumn = 4;

    /// <summary>Index of the community-rarity column.</summary>
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

    /// <summary>"Yes" for a collected mount, and an empty cell otherwise: a column of "No" is noise.</summary>
    /// <param name="item">The mount to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string OwnedText(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Owned ? "Yes" : string.Empty;
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

        if (item.Community?.OwnedPercent is not { } percent || double.IsNaN(percent))
        {
            return string.Empty;
        }

        var rounded = Math.Round(percent, 1, MidpointRounding.AwayFromZero);

        return rounded <= 0d
            ? string.Concat("<", MountRarity.Format(MountRarity.RoundingStep), "%")
            : string.Concat(MountRarity.Format(rounded), "%");
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
    /// carries. The table declares the current order as its default sort so that opening the table
    /// does not fire a sort change that would overwrite the player's choice.
    /// </summary>
    /// <param name="order">The order the list is currently in.</param>
    public static int ColumnFor(MountOrder order) =>
        order switch
        {
            MountOrder.OldestFirst or MountOrder.NewestFirst => PatchColumn,
            MountOrder.Seats => SeatsColumn,
            MountOrder.OwnedFirst => OwnedColumn,
            MountOrder.RarestFirst => RarityColumn,
            MountOrder.Cheapest => PriceColumn,
            _ => NameColumn,
        };

    /// <summary>
    /// The order a header click asks for, or null for a column that has no order of its own (the
    /// acquire method: "Trial first" is not a question anyone asks).
    /// </summary>
    /// <remarks>
    /// This deliberately maps onto the existing <see cref="MountOrder"/> values instead of sorting the
    /// rows here: one ordering implementation is what keeps the table, the grid and the Order combo from
    /// disagreeing, which is the same reason the patch parser was unified.
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
            RarityColumn => MountOrder.RarestFirst,
            PriceColumn => MountOrder.Cheapest,
            _ => null,
        };
}
