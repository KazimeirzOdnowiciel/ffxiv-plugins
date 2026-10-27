using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

/// <summary>A listing that disappeared from a retainer between checks - sold, or withdrawn.</summary>
public sealed record SoldEntry(uint ItemId, bool Hq, uint Price, uint Quantity, ulong RetainerId, DateTime At)
{
    public decimal Total => this.Price * this.Quantity;
}

/// <summary>One of your own market listings, as seen on the board.</summary>
public sealed record CapturedListing(
    uint ItemId,
    bool Hq,
    uint PricePerUnit,
    uint Quantity,
    ulong ListingId,
    ulong RetainerId,
    string RetainerName,
    int CityId,
    DateTime SeenUtc);

public enum ListingStatus
{
    Unpriced,
    Cheapest,
    AtMarket,
    AboveMarket,
}

public sealed record ListingReview(
    CapturedListing Listing,
    ListingStatus Status,
    decimal MarketMin,
    double PriceDeltaPercent,
    double DaysToSell,
    string Explain,
    decimal SuggestedPrice = 0m,
    double DaysIfRepriced = double.PositiveInfinity,
    string Advice = "",
    bool IsProbablySold = false)
{
    public bool IsHealthy => this.Status is ListingStatus.Cheapest or ListingStatus.AtMarket;

    public bool NeedsReprice => this.Status == ListingStatus.AboveMarket;
}

/// <summary>
/// Your own listings are discovered a few at a time: the game only tells us about listings for the
/// item currently open on the market board, so the book is built by browsing. One entry per
/// (retainer, item, quality) - repricing updates the slot instead of piling up history.
/// </summary>
public static class ListingBook
{
    public const int MaxEntries = 300;

    /// <summary>The game encodes high-quality item ids as base id + 1,000,000.</summary>
    public const uint HqItemIdOffset = 1_000_000u;

    public static uint NormalizeItemId(uint itemId, out bool hqEncoded)
    {
        hqEncoded = itemId >= HqItemIdOffset;
        return hqEncoded ? itemId - HqItemIdOffset : itemId;
    }

    /// <summary>
    /// Repairs entries captured before ids were normalised: strips the HQ offset, marks them HQ,
    /// and merges any slot that becomes a duplicate with a newer capture.
    /// </summary>
    public static int RepairEncodedIds(List<CapturedListing> book)
    {
        var repaired = 0;
        var merged = new Dictionary<(ulong Retainer, uint Item, bool Hq), CapturedListing>();

        foreach (var entry in book)
        {
            var itemId = NormalizeItemId(entry.ItemId, out var hqEncoded);
            if (hqEncoded)
                repaired++;

            var fixedEntry = hqEncoded ? entry with { ItemId = itemId, Hq = true } : entry;
            var key = (fixedEntry.RetainerId, fixedEntry.ItemId, fixedEntry.Hq);

            if (!merged.TryGetValue(key, out var existing) || existing.SeenUtc < fixedEntry.SeenUtc)
                merged[key] = fixedEntry;
        }

        if (repaired == 0 && merged.Count == book.Count)
            return 0;

        book.Clear();
        book.AddRange(merged.Values.OrderBy(entry => entry.SeenUtc));
        return repaired;
    }

    public static bool Upsert(List<CapturedListing> book, CapturedListing listing)
    {
        var index = book.FindIndex(entry =>
            entry.RetainerId == listing.RetainerId
            && entry.ItemId == listing.ItemId
            && entry.Hq == listing.Hq);

        if (index >= 0)
        {
            var existing = book[index];
            if (existing.PricePerUnit == listing.PricePerUnit
                && existing.Quantity == listing.Quantity
                && existing.ListingId == listing.ListingId)
            {
                // Same slot, same numbers: only the timestamp moves, no need to save the config.
                book[index] = existing with { SeenUtc = listing.SeenUtc };
                return false;
            }

            book.RemoveAt(index);
        }

        book.Add(listing);

        if (book.Count > MaxEntries)
        {
            book.Sort((a, b) => b.SeenUtc.CompareTo(a.SeenUtc));
            book.RemoveRange(MaxEntries, book.Count - MaxEntries);
        }

        return true;
    }

    public static int ForgetOlderThan(List<CapturedListing> book, TimeSpan age, DateTime now)
    {
        var cutoff = now - age;
        return book.RemoveAll(entry => entry.SeenUtc < cutoff);
    }

    /// <summary>A listing that has not been seen for this long is probably sold or expired.</summary>
    public static readonly TimeSpan ProbablySoldAfter = TimeSpan.FromDays(7);

    /// <summary>
    /// Compares one of your listings with the current market and turns that into an instruction.
    /// The queue model is the same one the scorer uses: anyone at or below your price is ahead.
    /// </summary>
    public static ListingReview Review(
        CapturedListing listing,
        MarketQuote? quote,
        SlotParameters parameters,
        DateTime? now = null)
    {
        var timestamp = now ?? DateTime.UtcNow;
        var age = timestamp - listing.SeenUtc;
        var probablySold = age > ProbablySoldAfter;

        if (quote is null || quote.MinListing <= 0)
        {
            return new ListingReview(
                listing,
                ListingStatus.Unpriced,
                0m,
                0,
                double.PositiveInfinity,
                "no market data for this item yet",
                Advice: probablySold
                    ? $"not seen in {age.TotalDays:0}d - sold or expired?"
                    : "no listings on this world yet");
        }

        var min = quote.MinListing;
        var delta = min <= 0m ? 0 : (double)((listing.PricePerUnit - min) / min);

        var status = listing.PricePerUnit < min
            ? ListingStatus.Cheapest
            : listing.PricePerUnit == min
                ? ListingStatus.AtMarket
                : ListingStatus.AboveMarket;

        var competitors = status == ListingStatus.AboveMarket
            ? Math.Max(0, quote.DistinctSellers)
            : Math.Max(0, quote.SellersAtMinPrice);

        var share = Math.Min(SlotModel.MaxAssumedShare, 1.0 / (competitors + 1));
        var unitsPerDay = quote.DailyVelocity * share;
        var days = unitsPerDay > 0 ? listing.Quantity / unitsPerDay : double.PositiveInfinity;

        // What the listing would look like at the price this strategy would choose today.
        var suggested = Math.Max(1m, SlotModel.ChoosePrice(quote, parameters.Strategy));
        var repricedCompetitors = Math.Max(0, SlotModel.Competitors(quote, suggested, parameters.Strategy));
        var repricedShare = Math.Min(SlotModel.MaxAssumedShare, 1.0 / (repricedCompetitors + 1));
        var repricedUnits = quote.DailyVelocity * repricedShare;
        var repricedDays = repricedUnits > 0
            ? listing.Quantity / repricedUnits
            : double.PositiveInfinity;

        var explain = status switch
        {
            ListingStatus.Cheapest => "you are the cheapest listing",
            ListingStatus.AtMarket => "at the market minimum, sharing the queue",
            _ => $"market minimum is {Money.Gil(min)}; yours is {delta:P0} above it",
        };

        var advice = status switch
        {
            ListingStatus.Cheapest => "cheapest - leave it",
            ListingStatus.AtMarket => "at minimum - sharing queue",
            _ => $"reprice {Money.Gil(suggested)} ({Money.Days(repricedDays)} vs {Money.Days(days)})",
        };

        if (probablySold)
            advice = $"not seen {age.TotalDays:0}d - sold?";

        return new ListingReview(
            listing,
            status,
            min,
            delta,
            days,
            explain,
            suggested,
            repricedDays,
            advice,
            probablySold);
    }
}
