using System;
using System.Collections.Generic;

namespace GilLedger.Core;

/// <summary>Profit-and-loss rollups over the recorded entries. Kept allocation-free because the UI calls these every frame.</summary>
public static class Pnl
{
    public sealed record DayRow(DateTime Day, long Income, long Expense, long Net);

    public sealed record KindRow(GilKind Kind, long Total);

    public static long Net(IReadOnlyList<GilEntry> entries)
    {
        long total = 0;
        for (var index = 0; index < entries.Count; index++)
            total += entries[index].Amount;

        return total;
    }

    public static long TodayNet(IReadOnlyList<GilEntry> entries)
    {
        var today = DateTime.Today;
        long net = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Utc.ToLocalTime().Date == today)
                net += entry.Amount;
        }

        return net;
    }

    public static List<DayRow> ByDay(IReadOnlyList<GilEntry> entries, int days)
    {
        var buckets = new Dictionary<DateTime, (long Income, long Expense)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var day = entry.Utc.ToLocalTime().Date;
            buckets.TryGetValue(day, out var acc);
            if (entry.Amount > 0)
                acc.Income += entry.Amount;
            else
                acc.Expense += entry.Amount;
            buckets[day] = acc;
        }

        var rows = new List<DayRow>(buckets.Count);
        foreach (var pair in buckets)
            rows.Add(new DayRow(pair.Key, pair.Value.Income, pair.Value.Expense, pair.Value.Income + pair.Value.Expense));

        rows.Sort((a, b) => b.Day.CompareTo(a.Day));
        if (rows.Count > days)
            rows.RemoveRange(days, rows.Count - days);

        return rows;
    }

    public static List<KindRow> ByKind(IReadOnlyList<GilEntry> entries)
    {
        var totals = new long[Enum.GetValues<GilKind>().Length];
        for (var index = 0; index < entries.Count; index++)
            totals[(int)entries[index].Kind] += entries[index].Amount;

        var rows = new List<KindRow>();
        for (var kind = 0; kind < totals.Length; kind++)
        {
            if (totals[kind] != 0)
                rows.Add(new KindRow((GilKind)kind, totals[kind]));
        }

        rows.Sort((a, b) => Math.Abs(b.Total).CompareTo(Math.Abs(a.Total)));
        return rows;
    }
}
