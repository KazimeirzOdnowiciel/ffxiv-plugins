using System;
using System.Collections.Generic;
using System.Linq;
using SlotYield.Core;

namespace SlotYield.Market;

/// <summary>
/// Material prices, best source first:
/// 1. the world quote we already paid for (exact, this world),
/// 2. the aggregated row from stage 1 (DC-wide, covers every marketable item),
/// 3. the vendor price from the game's item sheet.
/// </summary>
public sealed class CompositePriceSource : IPriceSource
{
    private readonly Dictionary<uint, MarketQuote> quotes;
    private readonly Dictionary<uint, AggregateRow> aggregates;
    private readonly Func<uint, decimal?>? vendorPrice;

    public CompositePriceSource(
        IEnumerable<MarketQuote> quotes,
        IEnumerable<AggregateRow> aggregates,
        Func<uint, decimal?>? vendorPrice = null)
    {
        this.quotes = new Dictionary<uint, MarketQuote>();
        foreach (var quote in quotes.OrderBy(q => q.Quality == Quality.Nq ? 0 : 1))
            this.quotes.TryAdd(quote.ItemId, quote);

        this.aggregates = new Dictionary<uint, AggregateRow>();
        foreach (var row in aggregates)
            this.aggregates.TryAdd(row.ItemId, row);

        this.vendorPrice = vendorPrice;
    }

    public decimal? UnitPrice(uint itemId)
    {
        if (this.quotes.TryGetValue(itemId, out var quote) && quote.MinListing > 0)
            return quote.MinListing;

        if (this.aggregates.TryGetValue(itemId, out var row))
        {
            if (row.NqAverage > 0)
                return row.NqAverage;

            if (row.NqMin > 0)
                return row.NqMin;

            if (row.HqAverage > 0)
                return row.HqAverage;

            if (row.HqMin > 0)
                return row.HqMin;
        }

        return this.vendorPrice?.Invoke(itemId);
    }
}
