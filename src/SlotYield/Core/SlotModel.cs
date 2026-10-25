using System;

namespace SlotYield.Core;

public enum Quality
{
    Nq,
    Hq,
}

/// <summary>
/// Everything the model needs to know about one item on one world, for one quality.
/// Pure data: no HTTP, no game types. This is what the tests feed.
/// </summary>
public sealed record MarketPriceStep(decimal Price, int Listings);

public sealed record MarketQuote
{
    public required uint ItemId { get; init; }

    public Quality Quality { get; init; }

    /// <summary>Cheapest listing price per unit.</summary>
    public decimal MinListing { get; init; }

    /// <summary>Average price actually paid in the sampled sales window.</summary>
    public decimal AverageSale { get; init; }

    /// <summary>Units sold per day on this world, for this quality.</summary>
    public double DailyVelocity { get; init; }

    public int ListingCount { get; init; }

    /// <summary>Distinct retainers with a listing of this item/quality.</summary>
    public int DistinctSellers { get; init; }

    /// <summary>Distinct retainers listing at exactly <see cref="MinListing"/>.</summary>
    public int SellersAtMinPrice { get; init; }

    public int HistorySamples { get; init; }

    public double HistorySpanDays { get; init; }

    /// <summary>
    /// Share of expensive single-unit sales that happened within a minute of each other at nearly the
    /// same price. High values usually mean gil transfer through the market board, not demand.
    /// </summary>
    public double WashTradeShare { get; init; }

    /// <summary>The cheapest few distinct prices on the board, cheapest first. Empty on old caches.</summary>
    public MarketPriceStep[] CheapestSteps { get; init; } = Array.Empty<MarketPriceStep>();

    /// <summary>Daily quantity-weighted prices for the last 30 days (empty when too sparse).</summary>
    public decimal[] PriceHistory { get; init; } = Array.Empty<decimal>();

    /// <summary>Percent change between the first and last third of the price history.</summary>
    public double TrendPercent { get; init; }

    public DateTime FetchedUtc { get; init; }

    public DateTime? LastUploadUtc { get; init; }

    public bool HasData => this.MinListing > 0 && this.DailyVelocity > 0;

    public double DataAgeMinutes => Math.Max(0, (DateTime.UtcNow - (this.LastUploadUtc ?? this.FetchedUtc)).TotalMinutes);
}

public sealed record SlotParameters
{
    public PricingStrategy Strategy { get; init; } = PricingStrategy.MatchMinimum;

    /// <summary>Fraction withheld by the market board, e.g. 0.03.</summary>
    public decimal TaxRate { get; init; } = 0.03m;

    public int StackSize { get; init; } = 99;

    public int DeadAfterDays { get; init; } = 45;

    public double MinimumVelocity { get; init; } = 0.2;
}

public sealed record SlotScore
{
    public required MarketQuote Quote { get; init; }

    public decimal ListPrice { get; init; }

    public decimal NetPerUnit { get; init; }

    public double ExpectedShare { get; init; }

    public double UnitsPerDay { get; init; }

    public decimal GilPerDay { get; init; }

    /// <summary>What one unit costs you to obtain, when we know.</summary>
    public decimal? CostPerUnit { get; init; }

    /// <summary>Net sale price minus acquisition cost.</summary>
    public decimal ProfitPerUnit { get; init; }

    /// <summary>Units captured per day times profit per unit. This is the number a crafter cares about.</summary>
    public decimal ProfitPerDay { get; init; }

    /// <summary>Some materials had no price, so the cost is a lower bound on the real cost.</summary>
    public bool CostIsPartial { get; init; }

    public string CostSource { get; init; } = "revenue only";

    /// <summary>Human-readable cost breakdown, e.g. "3 crafts -> 3 units, 5/5 materials priced".</summary>
    public string CostDetail { get; init; } = string.Empty;

    public decimal CapitalPerSlot { get; init; }

    /// <summary>Days to clear a full stack at the expected rate.</summary>
    public double DaysToClear { get; init; }

    public bool IsDead { get; init; }

    /// <summary>Volume looks manufactured. Not a slot to chase.</summary>
    public bool IsSuspect { get; init; }

    /// <summary>The price is meaningfully lower than it was a month ago.</summary>
    public bool IsFalling { get; init; }

    /// <summary>How much to trust this row: freshness x sample size x market depth, 0..1.</summary>
    public double Confidence { get; init; }

    /// <summary>The underlying market data is old enough that the price may no longer hold.</summary>
    public bool IsStale { get; init; }

    public double TrendPercent { get; init; }

    /// <summary>You would be the only seller at this price.</summary>
    public bool IsThin { get; init; }

    public string Verdict { get; init; } = string.Empty;

    public string Explain { get; init; } = string.Empty;
}

/// <summary>
/// The economics of one market slot.
///
/// A slot earns: (units you capture per day) x (net price per unit).
/// You capture a share of the market equal to 1 / (sellers you are queued behind + 1),
/// because buyers buy the cheapest listings first and everyone at your price is in the same queue.
/// </summary>
public static class SlotModel
{
    public const int MaxStack = 99;

    /// <summary>Never assume more than half of a market, even with zero competition.</summary>
    public const double MaxAssumedShare = 0.5;

    /// <summary>Market data older than this is treated as a guess, not a quote.</summary>
    public const double StaleAfterMinutes = 360;

    /// <summary>
    /// How much to trust a row, 0..1. Fresh uploads score high; a day-old board scores near zero.
    /// A market with no sale samples is a listing, not a market, so it scores zero regardless.
    /// </summary>
    public static double Confidence(MarketQuote quote)
    {
        if (!quote.HasData || quote.HistorySamples == 0)
            return 0;

        var age = quote.DataAgeMinutes;
        var ageFactor = age <= 30
            ? 1.0
            : Math.Clamp(1.0 - ((age - 30) / ((24 * 60) - 30)), 0, 1);

        var sampleFactor = Math.Clamp(quote.HistorySamples / 10.0, 0, 1);

        // A shallow market is still a real market; it just moves more on one buyer.
        var depthFactor = 0.5 + (Math.Clamp(quote.DistinctSellers / 3.0, 0, 1) * 0.5);

        return Math.Round(ageFactor * sampleFactor * depthFactor, 3);
    }

    public static SlotScore Score(MarketQuote quote, SlotParameters parameters)
    {
        var price = ChoosePrice(quote, parameters.Strategy);
        var competitors = Competitors(quote, price, parameters.Strategy);

        // We join the queue behind the sellers already at (or below) our price.
        var share = Math.Min(MaxAssumedShare, 1.0 / (competitors + 1));

        var unitsPerDay = Math.Min(quote.DailyVelocity * share, parameters.StackSize);
        var netPerUnit = Math.Round(price * (1m - parameters.TaxRate), 0, MidpointRounding.AwayFromZero);
        var gilPerDay = Math.Round((decimal)unitsPerDay * netPerUnit, 0);
        var capital = parameters.StackSize * price;
        var daysToClear = unitsPerDay > 0 ? parameters.StackSize / unitsPerDay : double.PositiveInfinity;

        var dead = !quote.HasData
                   || quote.DailyVelocity < parameters.MinimumVelocity
                   || daysToClear > parameters.DeadAfterDays;

        var thin = quote.DistinctSellers <= 1;
        var suspect = quote.WashTradeShare >= 0.5;

        return new SlotScore
        {
            Quote = quote,
            ListPrice = price,
            NetPerUnit = netPerUnit,
            ExpectedShare = share,
            UnitsPerDay = unitsPerDay,
            GilPerDay = dead ? 0m : gilPerDay,
            CapitalPerSlot = capital,
            DaysToClear = daysToClear,
            IsDead = dead,
            IsSuspect = suspect,
            IsFalling = quote.TrendPercent < -0.10 && !dead,
            Confidence = Confidence(quote),
            IsStale = quote.DataAgeMinutes > StaleAfterMinutes || quote.HistorySamples == 0,
            TrendPercent = quote.TrendPercent,
            IsThin = thin,
            Verdict = suspect ? "suspicious volume" : VerdictFor(quote, dead, thin, daysToClear),
            Explain = Explain(quote, parameters, price, competitors, share, unitsPerDay, netPerUnit, daysToClear),
        };
    }

    /// <summary>
    /// Turns a revenue score into a profit score. When no cost is known (revenue mode, or nothing
    /// priceable), profit falls back to revenue and is flagged so the UI can say so.
    /// </summary>
    public static SlotScore ApplyCost(SlotScore score, ItemCost? cost, bool expectCost, int stackSize = 0)
    {
        if (cost is null)
        {
            return score with
            {
                CostPerUnit = null,
                ProfitPerUnit = score.NetPerUnit,
                ProfitPerDay = score.IsDead ? 0m : score.GilPerDay,
                CostSource = "revenue only",
                CostIsPartial = expectCost,
            };
        }

        var profitPerUnit = score.NetPerUnit - cost.PerUnit;
        var profitPerDay = (decimal)score.UnitsPerDay * profitPerUnit;

        return score with
        {
            CostPerUnit = cost.PerUnit,
            ProfitPerUnit = profitPerUnit,
            ProfitPerDay = score.IsDead ? 0m : profitPerDay,
            CapitalPerSlot = stackSize > 0 ? cost.PerUnit * stackSize : score.CapitalPerSlot,
            CostSource = cost.Source,
            CostDetail = cost.Detail,
            CostIsPartial = cost.Partial,
        };
    }

    public static decimal ChoosePrice(MarketQuote quote, PricingStrategy strategy)
    {
        var min = quote.MinListing;
        var avg = quote.AverageSale > 0 ? quote.AverageSale : min;

        return strategy switch
        {
            PricingStrategy.UndercutMinimum => Math.Max(1m, min - 1m),
            PricingStrategy.MatchAverageSale => Math.Max(min, avg),
            _ => Math.Max(1m, min),
        };
    }

    public static int Competitors(MarketQuote quote, decimal listPrice, PricingStrategy strategy)
    {
        // Undercutting puts us alone at a new price; the share cap still applies.
        if (strategy == PricingStrategy.UndercutMinimum)
            return 0;

        // Anyone listing at or below our price is ahead of us in the queue.
        return listPrice <= quote.MinListing
            ? Math.Max(0, quote.SellersAtMinPrice)
            : Math.Max(0, quote.DistinctSellers);
    }

    private static string VerdictFor(MarketQuote quote, bool dead, bool thin, double days)
    {
        if (!quote.HasData)
            return "no sales data";

        if (dead)
            return quote.DailyVelocity < 0.01 ? "unsold" : "dead slot";

        if (days <= 7)
            return "fast mover";

        if (days <= 21)
            return "steady";

        return thin ? "slow / sole seller" : "slow";
    }

    private static string Explain(
        MarketQuote quote,
        SlotParameters parameters,
        decimal price,
        int competitors,
        double share,
        double unitsPerDay,
        decimal netPerUnit,
        double daysToClear)
    {
        var shareText = competitors == 0
            ? "no one at your price, assuming a 50% cap"
            : $"{competitors} seller{(competitors == 1 ? string.Empty : "s")} at {Money.Gil(price)}, share {share:P0}";

        return $"{quote.DailyVelocity:0.0} sales/day -> {unitsPerDay:0.00} units/day ({shareText}) " +
               $"x {Money.Gil(netPerUnit)} net ({parameters.TaxRate:P0} tax) = {Money.Gil((decimal)unitsPerDay * netPerUnit)}/day; " +
               $"a {parameters.StackSize} stack clears in {daysToClear:0.#}d.";
    }
}
