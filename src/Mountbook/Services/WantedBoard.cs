using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mountbook.Services;

/// <summary>How the wanted board orders its rows; the player picks one and it is persisted.</summary>
public enum MountBoardSort
{
    /// <summary>Oldest patch first: the mounts that have been waiting longest.</summary>
    Patch,

    /// <summary>Cheapest known price first: what gil can actually shortcut.</summary>
    Price,
}

/// <summary>One line of the wanted board: a mount still to collect, where it comes from, and the count.</summary>
/// <param name="Id">The mount id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Source">The first "How to obtain" line's detail, or empty when the dataset has none.</param>
/// <param name="Patch">The patch the dataset records, or empty.</param>
/// <param name="Have">Tokens banked.</param>
/// <param name="Want">The target: the player's own, else the shop price, else zero.</param>
/// <param name="Price">The cheapest known market price in gil, or zero when none has been read.</param>
/// <param name="UpdatedAt">Unix seconds the record last changed, or zero when the player never touched it.</param>
public sealed record BoardRow(
    uint Id,
    string Name,
    string Source,
    string Patch,
    int Have,
    int Want,
    int Price,
    long UpdatedAt)
{
    /// <summary>How full this row's bar is.</summary>
    public float Fraction => MountGrind.Fraction(Have, Want);

    /// <summary>True when a price has been read for this mount, so the row may show one.</summary>
    public bool PriceKnown => Price > 0;
}

/// <summary>
/// The wanted board's rows: the mounts the player starred and does not own, in the order they asked for.
/// </summary>
/// <remarks>
/// <para>
/// Pure and Dalamud-free (T1). Prices arrive through a function rather than a dictionary because the
/// caller is the only part that can translate a mount id into a whistle item id - and because the board
/// must never ask the market for one: a price is shown when it is already known, and a sort by price puts
/// the unknown ones last instead of fetching them (N2).
/// </para>
/// <para>
/// A wanted mount that is now collected leaves the board on the same frame the catalog notices: the board
/// is a to-do list, and something done is noise on it.
/// </para>
/// </remarks>
public static class WantedBoard
{
    /// <summary>
    /// The rows to draw: wanted and unowned, in <paramref name="sort"/> order.
    /// </summary>
    /// <param name="items">Every mount the catalog carries, filtered and ordered by the caller.</param>
    /// <param name="wanted">The player's hunting list.</param>
    /// <param name="grinds">The saved progress records, keyed by mount id, or null when none are saved.</param>
    /// <param name="prices">Resolves the cheapest known price of a mount in gil, or zero when none is known.</param>
    /// <param name="sort">Which order the rows are in.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IReadOnlyList<BoardRow> Build(
        IReadOnlyList<MountShelfItem> items,
        IReadOnlySet<uint> wanted,
        IReadOnlyDictionary<uint, MountGrindRecord>? grinds,
        Func<uint, int> prices,
        MountBoardSort sort)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(prices);

        var rows = new List<BoardRow>(wanted.Count);

        foreach (MountShelfItem item in items)
        {
            if (item.Owned || !wanted.Contains(item.Id))
            {
                continue;
            }

            MountGrindRecord? grind = grinds is not null && grinds.TryGetValue(item.Id, out MountGrindRecord? saved)
                ? saved
                : null;

            rows.Add(new BoardRow(
                item.Id,
                item.Name,
                FirstSource(item),
                item.Patch ?? string.Empty,
                MountGrind.Have(grind),
                MountGrind.Target(grind, MountCosts.FirstOf(item.Sources)),
                prices(item.Id),
                grind?.UpdatedAt ?? 0));
        }

        rows.Sort((left, right) => sort switch
        {
            MountBoardSort.Price => CompareByPrice(left, right),
            _ => CompareByPatch(left, right),
        });

        return rows;
    }

    /// <summary>
    /// The row's patch as the board words it, or the "unrecorded" line for a mount the dataset has no patch
    /// for. Here rather than in the view because a view may hold no formatting of its own.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public static string PatchText(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Patch.Length > 0 ? Loc.F("board.patch", row.Patch) : Loc.T("board.patch.unknown");
    }

    /// <summary>
    /// The row's cheapest known price, grouped, or the line that says none has been read.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public static string PriceText(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.PriceKnown
            ? Loc.F("board.price", row.Price.ToString("N0", CultureInfo.InvariantCulture))
            : Loc.T("board.price.unknown");
    }

    /// <summary>
    /// The row's progress as one line: the count and what is left, or the honest alternative for a mount the
    /// dataset records no token price for. A "0 / 0 tokens" line would read as a target nobody set.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public static string ProgressText(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Want > 0
            ? Loc.F("board.progress", MountGrind.Label(row.Have, row.Want), MountGrind.Remaining(row.Have, row.Want))
            : Loc.T("board.progress.none");
    }

    /// <summary>
    /// The first source line's detail: the one line a to-do list can carry without becoming the detail
    /// pane. Empty for a mount whose row the dataset has no source for, which the board draws as nothing
    /// rather than as a placeholder.
    /// </summary>
    /// <param name="item">The mount.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string FirstSource(MountShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Sources.Count == 0 ? string.Empty : item.Sources[0].Detail;
    }

    /// <summary>Oldest patch first, then name; a mount with no recorded patch sorts last.</summary>
    private static int CompareByPatch(BoardRow left, BoardRow right)
    {
        decimal? leftPatch = MountExpansionRules.PatchValue(left.Patch);
        decimal? rightPatch = MountExpansionRules.PatchValue(right.Patch);

        if (leftPatch is null || rightPatch is null)
        {
            if (leftPatch is not null)
            {
                return -1;
            }

            return rightPatch is not null ? 1 : CompareByName(left, right);
        }

        int byPatch = leftPatch.Value.CompareTo(rightPatch.Value);

        return byPatch != 0 ? byPatch : CompareByName(left, right);
    }

    /// <summary>Cheapest first, then name; a mount nobody has priced sorts last, because unknown is not cheap.</summary>
    private static int CompareByPrice(BoardRow left, BoardRow right)
    {
        if (left.Price <= 0 || right.Price <= 0)
        {
            if (left.Price > 0)
            {
                return -1;
            }

            return right.Price > 0 ? 1 : CompareByName(left, right);
        }

        int byPrice = left.Price.CompareTo(right.Price);

        return byPrice != 0 ? byPrice : CompareByName(left, right);
    }

    private static int CompareByName(BoardRow left, BoardRow right) =>
        string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
}
