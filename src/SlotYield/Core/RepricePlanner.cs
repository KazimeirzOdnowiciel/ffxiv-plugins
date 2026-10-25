using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

/// <summary>One listing that would be repriced, with both ends of the move.</summary>
public sealed record RepriceChange(
    uint ItemId,
    bool Hq,
    uint CurrentPrice,
    decimal TargetPrice,
    double DeltaPercent,
    double DaysNow,
    double DaysAfter,
    ulong RetainerId = 0)
{
    public decimal GilFreed => (this.CurrentPrice - this.TargetPrice) * 1m;
}

public sealed record RepricePlan(
    IReadOnlyList<RepriceChange> Changes,
    int SkippedForFloor,
    int SkippedForSmallMove)
{
    public bool IsEmpty => this.Changes.Count == 0;

    public decimal TotalDevaluation => this.Changes.Sum(change => change.GilFreed);
}

/// <summary>
/// Works out which of your listings are worth dropping, and to what price. Pure: it never touches
/// the game, which is what makes the plan reviewable before anything is applied.
/// </summary>
public static class RepricePlanner
{
    /// <summary>A listing more than this far below its current price is treated as a mistake.</summary>
    public const decimal LargeDropFloor = 0.5m;

    public static RepricePlan Build(
        IReadOnlyList<CapturedListing> listings,
        Func<uint, Quality, MarketQuote?> quoteFor,
        SlotParameters parameters,
        int minMoveGil = 0,
        int floorPercent = (int)(LargeDropFloor * 100))
    {
        var changes = new List<RepriceChange>();
        var skippedFloor = 0;
        var skippedSmall = 0;

        foreach (var listing in listings)
        {
            if (listing.PricePerUnit == 0)
                continue;

            var quality = listing.Hq ? Quality.Hq : Quality.Nq;
            var quote = quoteFor(listing.ItemId, quality);

            if (quote is null || !quote.HasData || quote.MinListing <= 0)
                continue;

            var target = Math.Max(1m, Math.Round(SlotModel.ChoosePrice(quote, parameters.Strategy)));

            if (target >= listing.PricePerUnit)
                continue;

            var delta = (double)((listing.PricePerUnit - target) / listing.PricePerUnit);
            var move = listing.PricePerUnit - target;

            if (move < Math.Max(0, minMoveGil))
            {
                skippedSmall++;
                continue;
            }

            if (floorPercent > 0 && target < listing.PricePerUnit * floorPercent / 100m)
            {
                skippedFloor++;
                continue;
            }

            var now = ListingBook.Review(listing, quote, parameters);
            var after = ListingBook.Review(listing with { PricePerUnit = (uint)target }, quote, parameters);

            changes.Add(new RepriceChange(
                listing.ItemId,
                listing.Hq,
                listing.PricePerUnit,
                target,
                delta,
                now.DaysToSell,
                after.DaysToSell,
                listing.RetainerId));
        }

        return new RepricePlan(
            changes.OrderByDescending(change => change.DeltaPercent).ToList(),
            skippedFloor,
            skippedSmall);
    }
}
