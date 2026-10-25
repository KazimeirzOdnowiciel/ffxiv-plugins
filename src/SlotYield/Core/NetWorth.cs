using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

public enum WorthValuation
{
    /// <summary>World (then DC) minimum listing: what the stack could liquidate for today.</summary>
    MinListing,

    /// <summary>Average sale price: what it has actually been selling for recently.</summary>
    AverageSale,
}

/// <summary>One owned stack, valued.</summary>
public sealed record WorthItem(uint ItemId, string Category, int Quantity, decimal Value);

/// <summary>One category of holdings, e.g. "Materials", valued at market.</summary>
public sealed record WorthSlice(string Category, decimal Value, int Items);

/// <summary>A point-in-time valuation of everything you own.</summary>
public sealed record WorthSnapshot(
    DateTime At,
    decimal Total,
    decimal Gil,
    IReadOnlyList<WorthSlice> Slices,
    int UnpricedItems,
    int Stacks,
    WorthValuation Mode,
    IReadOnlyList<WorthItem> Items);

/// <summary>One day of history, kept so the window can draw a trend line.</summary>
public sealed record WorthPoint(DateTime Day, decimal Total);

public static class NetWorth
{
    /// <summary>
    /// Values every stack at its market price and adds gil. Unpriced stacks are counted, not guessed:
    /// a missing price must not look like a free item.
    /// </summary>
    public static WorthSnapshot Compute(
        IEnumerable<(uint ItemId, int Quantity)> items,
        decimal gil,
        Func<uint, decimal?> priceOf,
        Func<uint, string> categoryOf,
        DateTime at,
        WorthValuation mode = WorthValuation.MinListing)
    {
        var categories = new Dictionary<string, (decimal Value, int Items)>(StringComparer.OrdinalIgnoreCase);
        var valued = new List<WorthItem>();
        var total = 0m;
        var unpriced = 0;
        var stacks = 0;

        foreach (var (itemId, quantity) in items)
        {
            if (itemId == 0 || quantity <= 0)
                continue;

            stacks++;

            var price = priceOf(itemId);

            if (price is null || price <= 0)
            {
                unpriced++;
                continue;
            }

            var value = price.Value * quantity;
            total += value;

            var category = categoryOf(itemId);
            if (string.IsNullOrWhiteSpace(category))
                category = "Other";

            var existing = categories.GetValueOrDefault(category);
            categories[category] = (existing.Value + value, existing.Items + quantity);
            valued.Add(new WorthItem(itemId, category, quantity, value));
        }

        var slices = categories
            .OrderByDescending(pair => pair.Value.Value)
            .Select(pair => new WorthSlice(pair.Key, pair.Value.Value, pair.Value.Items))
            .ToList();

        return new WorthSnapshot(
            at,
            total + gil,
            gil,
            slices,
            unpriced,
            stacks,
            mode,
            valued.OrderByDescending(item => item.Value).ToList());
    }

    /// <summary>Change against the most recent earlier point, as a fraction.</summary>
    public static double ChangeSince(IReadOnlyList<WorthPoint> history, decimal current)
    {
        if (history.Count == 0)
            return 0;

        var previous = history[^1].Total;

        if (previous <= 0)
            return 0;

        return (double)((current - previous) / previous);
    }
}
