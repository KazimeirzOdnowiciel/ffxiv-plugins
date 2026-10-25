using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Market;

public readonly record struct SalePoint(long Timestamp, decimal Price, int Quantity);

/// <summary>
/// Turns a bag of sales into a 30-day daily price series and a trend number.
///
/// Daily values are quantity-weighted averages, with the extreme 10% of sales dropped first so a
/// single 1-gil troll sale or a panic undercut does not define a day. Missing days carry the last
/// known price forward (and the first known price backward), because "no sale today" is not "price
/// is zero".
/// </summary>
public static class PriceHistory
{
    public const int DefaultDays = 30;

    /// <summary>Below this many days with sales, a series says nothing.</summary>
    public const int MinDaysWithData = 3;

    /// <summary>Sales this far outside the median are noise, not market.</summary>
    public const decimal LowerFactor = 0.25m;

    public const decimal UpperFactor = 4m;

    public static decimal[] Build(IReadOnlyList<SalePoint> sales, long referenceUnix, int days = DefaultDays)
    {
        if (sales.Count == 0 || days <= 0)
            return Array.Empty<decimal>();

        var usable = TrimExtremes(sales);

        var sums = new decimal[days];
        var weights = new int[days];

        foreach (var sale in usable)
        {
            var ageDays = (referenceUnix - sale.Timestamp) / 86_400.0;
            if (ageDays < 0 || ageDays >= days)
                continue;

            var index = days - 1 - (int)ageDays;
            sums[index] += sale.Price * sale.Quantity;
            weights[index] += sale.Quantity;
        }

        var known = new decimal?[days];
        var daysWithData = 0;

        for (var i = 0; i < days; i++)
        {
            if (weights[i] <= 0)
                continue;

            known[i] = sums[i] / weights[i];
            daysWithData++;
        }

        if (daysWithData < MinDaysWithData)
            return Array.Empty<decimal>();

        // Carry forward, then backward, so the line is continuous.
        decimal? last = null;
        for (var i = 0; i < days; i++)
        {
            if (known[i] is { } value)
                last = value;
            else if (last is { } carried)
                known[i] = carried;
        }

        decimal? next = null;
        for (var i = days - 1; i >= 0; i--)
        {
            if (known[i] is { } value)
                next = value;
            else if (next is { } carried)
                known[i] = carried;
        }

        return known.Select(v => v ?? 0m).ToArray();
    }

    /// <summary>
    /// Percent change between the first third and the last third of the series.
    /// Positive means the item is getting more expensive.
    /// </summary>
    public static double TrendPercent(IReadOnlyList<decimal> series)
    {
        if (series.Count < 6)
            return 0;

        var third = series.Count / 3;
        if (third <= 0)
            return 0;

        var first = Mean(series, 0, third);
        var last = Mean(series, series.Count - third, third);

        if (first <= 0m)
            return 0;

        return (double)((last - first) / first);
    }

    public static IReadOnlyList<SalePoint> TrimExtremes(IReadOnlyList<SalePoint> sales)
    {
        if (sales.Count < 10)
            return sales;

        // A median band, not percentiles: a trending market should keep its endpoints, while a
        // single 1-gil phantom or a 999,999,999 troll sale falls outside five times the median.
        var prices = sales.Select(s => s.Price).OrderBy(p => p).ToList();
        var median = prices[prices.Count / 2];
        if (median <= 0m)
            return sales;

        var low = median * LowerFactor;
        var high = median * UpperFactor;

        var trimmed = sales.Where(s => s.Price >= low && s.Price <= high).ToList();
        return trimmed.Count > 0 ? trimmed : sales;
    }

    private static decimal Mean(IReadOnlyList<decimal> values, int start, int count)
    {
        if (count <= 0)
            return 0m;

        var total = 0m;
        for (var i = start; i < start + count && i < values.Count; i++)
            total += values[i];

        return total / count;
    }
}
