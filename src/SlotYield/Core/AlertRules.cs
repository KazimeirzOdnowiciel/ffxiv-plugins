using System;

namespace SlotYield.Core;

public enum AlertKind
{
    Undercut,
    PriceDrop,
    ProfitFloor,
}

public sealed record AlertCooldownEntry(uint ItemId, Quality Quality, AlertKind Kind, DateTime RaisedUtc);

public sealed record MarketAlert(
    AlertKind Kind,
    uint ItemId,
    Quality Quality,
    string Message,
    DateTime RaisedUtc);

/// <summary>
/// Pure alert rules. They decide *whether* something is worth saying; naming, cooldowns and chat
/// output belong to the caller, which keeps every rule testable without a game or a network.
/// </summary>
public static class AlertRules
{
    /// <summary>Fires when the market minimum sits meaningfully below one of your listings.</summary>
    public static MarketAlert? Undercut(
        CapturedListing listing,
        MarketQuote? quote,
        int undercutPercent,
        DateTime now)
    {
        if (quote is null || !quote.HasData || listing.PricePerUnit <= 0 || quote.MinListing <= 0)
            return null;

        var percent = Math.Clamp(undercutPercent, 0, 90);
        var threshold = listing.PricePerUnit * (100m - percent) / 100m;

        // A minimum equal to your price means you are the cheapest (or sharing it), not undercut.
        if (quote.MinListing >= threshold)
            return null;

        var gap = (listing.PricePerUnit - quote.MinListing) / listing.PricePerUnit;
        var quality = listing.Hq ? Quality.Hq : Quality.Nq;

        return new MarketAlert(
            AlertKind.Undercut,
            listing.ItemId,
            quality,
            $"undercut: market {Money.Gil(quote.MinListing)}, yours {Money.Gil(listing.PricePerUnit)} (market {gap:P0} below yours)",
            now);
    }

    /// <summary>Fires when the minimum has fallen by at least the configured share since last poll.</summary>
    public static MarketAlert? PriceDrop(
        uint itemId,
        Quality quality,
        MarketQuote? previous,
        MarketQuote? current,
        int dropPercent,
        DateTime now)
    {
        if (previous is null || current is null || !previous.HasData || !current.HasData)
            return null;

        if (previous.MinListing <= 0 || current.MinListing <= 0)
            return null;

        var drop = (previous.MinListing - current.MinListing) / previous.MinListing;
        var percent = Math.Clamp(dropPercent, 1, 90);

        if (drop * 100m < percent)
            return null;

        return new MarketAlert(
            AlertKind.PriceDrop,
            itemId,
            quality,
            $"price fell {drop:P0} to {Money.Gil(current.MinListing)}",
            now);
    }

    /// <summary>
    /// Fires when a pinned item crosses above the profit floor. "Previous" is the value from the
    /// last scan, so a pin sitting comfortably above the floor does not keep announcing itself.
    /// </summary>
    public static MarketAlert? ProfitFloor(
        uint itemId,
        Quality quality,
        decimal profitPerDay,
        decimal? previous,
        long floor,
        DateTime now)
    {
        if (profitPerDay < floor)
            return null;

        if (previous is { } earlier && earlier >= floor)
            return null;

        var was = previous is { } value
            ? $", was {Money.Gil(value)}/day"
            : string.Empty;

        return new MarketAlert(
            AlertKind.ProfitFloor,
            itemId,
            quality,
            $"now worth {Money.Gil(profitPerDay)}/day{was}",
            now);
    }

    public static bool CooldownElapsed(DateTime? lastRaised, DateTime now, TimeSpan cooldown)
        => lastRaised is null || now - lastRaised.Value >= cooldown;
}
