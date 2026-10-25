using System;
using System.Collections.Generic;
using System.Linq;
using SlotYield.Core;

namespace SlotYield.Market;

/// <summary>
/// Turns a raw Universalis quote into the model's <see cref="MarketQuote"/>.
/// Pure and heavily tested: this is where HQ/NQ get separated, where velocity is recomputed from the
/// sales actually observed, and where one absurd listing cannot poison a whole market.
/// </summary>
public static class QuoteFactory
{
    /// <summary>Sales older than this are ignored when measuring velocity.</summary>
    public const int MaxWindowDays = 30;

    /// <summary>Listings below this fraction of the median are treated as troll/stale noise.</summary>
    public const decimal OutlierFloor = 0.10m;

    public static MarketQuote? Build(QuotedItem item, Quality quality, DateTime fetchedUtc)
    {
        var listings = (item.Listings ?? new List<Listing>())
            .Where(l => l.PricePerUnit > 0 && l.Quantity > 0 && l.Hq == (quality == Quality.Hq))
            .ToList();

        if (listings.Count == 0)
            return null;

        listings = TrimOutliers(listings);
        var minPrice = listings.Min(l => l.PricePerUnit);

        var sellersAtMin = listings
            .Where(l => l.PricePerUnit == minPrice)
            .Select(SellerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var distinctSellers = listings
            .Select(SellerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var sales = (item.RecentHistory ?? new List<Sale>())
            .Where(s => s.Hq == (quality == Quality.Hq) && s.Quantity > 0 && s.PricePerUnit > 0 && s.Timestamp > 0)
            .ToList();

        var reference = item.LastUploadTime > 0
            ? item.LastUploadTime / 1000
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (velocity, spanDays, samples) = Measure(sales, reference);

        if (samples == 0 && item.RegularSaleVelocity > 0)
        {
            // No per-quality history at all. The API only gives a combined velocity, so split it by
            // how much of the board this quality represents. Giving both qualities the full number
            // would double-count the whole market.
            var allListings = (item.Listings ?? new List<Listing>())
                .Where(l => l.PricePerUnit > 0 && l.Quantity > 0)
                .ToList();

            var share = allListings.Count > 0 ? listings.Count / (double)allListings.Count : 0.5;
            velocity = item.RegularSaleVelocity * Math.Clamp(share, 0.05, 1.0);
            spanDays = 0;
        }

        var steps = listings
            .GroupBy(l => l.PricePerUnit)
            .OrderBy(group => group.Key)
            .Take(5)
            .Select(group => new MarketPriceStep(group.Key, group.Count()))
            .ToArray();

        var average = WeightedAveragePrice(sales, minPrice);

        var points = sales.Select(s => new SalePoint(s.Timestamp, s.PricePerUnit, s.Quantity)).ToList();
        var series = PriceHistory.Build(points, reference);
        var trend = PriceHistory.TrendPercent(series);

        return new MarketQuote
        {
            ItemId = item.ItemId,
            Quality = quality,
            MinListing = minPrice,
            AverageSale = average,
            DailyVelocity = velocity,
            ListingCount = listings.Count,
            DistinctSellers = distinctSellers,
            SellersAtMinPrice = sellersAtMin,
            CheapestSteps = steps,
            HistorySamples = samples,
            HistorySpanDays = spanDays,
            WashTradeShare = WashTradeShare(sales),
            PriceHistory = series,
            TrendPercent = trend,
            FetchedUtc = fetchedUtc,
            LastUploadUtc = item.LastUploadTime > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(item.LastUploadTime).UtcDateTime
                : null,
        };
    }

    /// <summary>
    /// Share of expensive, single-unit sales that sit within 90 seconds of the previous one at a
    /// near-identical price. Organic demand is spread out; gil transfer through the market board is
    /// a burst of one-unit trades. Cheap commodities are exempt, because they genuinely do sell in
    /// bursts of identical single units.
    /// </summary>
    public static double WashTradeShare(IReadOnlyList<Sale> sales)
    {
        const decimal ExpensiveThreshold = 100_000m;
        const long MaxGapSeconds = 90;
        const decimal MaxPriceDelta = 0.01m;

        var relevant = sales
            .Where(s => s.Quantity == 1 && s.PricePerUnit >= ExpensiveThreshold)
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (relevant.Count < 4)
            return 0;

        var suspicious = 0;
        for (var i = 1; i < relevant.Count; i++)
        {
            var gap = relevant[i].Timestamp - relevant[i - 1].Timestamp;
            var previous = Math.Max(1m, relevant[i - 1].PricePerUnit);
            var delta = Math.Abs(relevant[i].PricePerUnit - relevant[i - 1].PricePerUnit) / previous;

            if (gap <= MaxGapSeconds && delta <= MaxPriceDelta)
                suspicious++;
        }

        return suspicious / (double)(relevant.Count - 1);
    }

    /// <summary>
    /// Drops the 1-gil phantom listings that occasionally sit at the top of a board. They are real
    /// listings, but they are not the market: pricing a slot off them produces nonsense.
    /// </summary>
    public static List<Listing> TrimOutliers(List<Listing> listings)
    {
        if (listings.Count < 3)
            return listings;

        var median = Median(listings.Select(l => l.PricePerUnit).ToList());
        var floor = median * OutlierFloor;

        var sensible = listings.Where(l => l.PricePerUnit >= floor).ToList();
        return sensible.Count > 0 ? sensible : listings;
    }

    /// <summary>
    /// Velocity = units sold / observed span in days, where the span runs from the oldest sale to the
    /// data's upload time. A single sale ten days ago is 0.1/day, not 1/day.
    /// </summary>
    public static (double Velocity, double SpanDays, int Samples) Measure(IReadOnlyList<Sale> sales, long referenceUnix)
    {
        if (sales.Count == 0)
            return (0, 0, 0);

        var oldest = sales.Min(s => s.Timestamp);
        var spanDays = Math.Clamp((referenceUnix - oldest) / 86_400.0, 1.0, MaxWindowDays);

        var cutoff = referenceUnix - (long)(spanDays * 86_400);
        var window = sales.Where(s => s.Timestamp >= cutoff && s.Timestamp <= referenceUnix).ToList();
        if (window.Count == 0)
            window = sales.Where(s => s.Timestamp <= referenceUnix).ToList();
        if (window.Count == 0)
            window = sales.ToList();

        var units = window.Sum(s => (double)s.Quantity);
        return (units / spanDays, spanDays, window.Count);
    }

    public static decimal WeightedAveragePrice(IReadOnlyList<Sale> sales, decimal minListing)
    {
        if (sales.Count == 0)
            return 0m;

        var units = sales.Sum(s => (decimal)s.Quantity);
        if (units <= 0m)
            return 0m;

        var average = sales.Sum(s => s.PricePerUnit * s.Quantity) / units;

        // A stale or trolled history can be far out of line with the live board.
        return Math.Clamp(average, minListing * 0.5m, minListing * 10m);
    }

    public static decimal Median(List<decimal> values)
    {
        if (values.Count == 0)
            return 0m;

        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2m
            : sorted[middle];
    }

    private static string SellerKey(Listing listing)
    {
        return string.IsNullOrWhiteSpace(listing.RetainerName)
            ? $"unknown:{listing.RetainerCity}"
            : listing.RetainerName!;
    }
}
