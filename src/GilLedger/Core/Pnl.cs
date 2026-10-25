using System;
using System.Collections.Generic;
using System.Linq;

namespace GilLedger.Core;

/// <summary>Profit-and-loss rollups over the recorded entries.</summary>
public static class Pnl
{
    public sealed record DayRow(DateTime Day, long Income, long Expense, long Net);

    public sealed record KindRow(GilKind Kind, long Total);

    public static long Net(IEnumerable<GilEntry> entries) => entries.Sum(e => e.Amount);

    public static long Income(IEnumerable<GilEntry> entries) =>
        entries.Where(e => e.Amount > 0).Sum(e => e.Amount);

    public static long Expense(IEnumerable<GilEntry> entries) =>
        entries.Where(e => e.Amount < 0).Sum(e => e.Amount);

    public static IReadOnlyList<DayRow> ByDay(IEnumerable<GilEntry> entries, int days)
    {
        var local = entries
            .GroupBy(e => e.Utc.ToLocalTime().Date)
            .Select(g => new DayRow(
                g.Key,
                g.Where(e => e.Amount > 0).Sum(e => e.Amount),
                g.Where(e => e.Amount < 0).Sum(e => e.Amount),
                g.Sum(e => e.Amount)))
            .OrderByDescending(r => r.Day)
            .Take(days)
            .ToList();

        return local;
    }

    public static IReadOnlyList<KindRow> ByKind(IEnumerable<GilEntry> entries) =>
        entries
            .GroupBy(e => e.Kind)
            .Select(g => new KindRow(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(r => Math.Abs(r.Total))
            .ToList();
}
