using System;
using System.Collections.Generic;
using SlotYield.Core;

namespace SlotYield.Market;

public sealed record ScanOptions
{
    public required string World { get; init; }

    public SlotParameters Model { get; init; } = new();

    public int ShortlistSize { get; init; } = 400;

    public int BatchSize { get; init; } = 100;

    public int MaxConcurrency { get; init; } = 4;

    public TimeSpan CacheMaxAge { get; init; } = TimeSpan.FromHours(6);

    public bool UseCache { get; init; } = true;

    /// <summary>Items to price even if they do not make the shortlist (pinned slots, lookups).</summary>
    public IReadOnlyList<uint> ExtraItemIds { get; init; } = Array.Empty<uint>();

    /// <summary>How item acquisition cost is estimated. Revenue disables cost entirely.</summary>
    public CostMode CostMode { get; init; } = CostMode.Revenue;
}

public sealed record ScanProgress(string Phase, int Done, int Total, string Detail);

public sealed record SlotCandidate(uint ItemId, Quality Quality, MarketQuote Quote, SlotScore Score, ItemCost? Cost = null);

public sealed record ScanStats(
    int MarketableItems,
    int AggregatedItems,
    int PricedItems,
    int CostedItems,
    int CraftCostItems,
    int MarketCostItems,
    int PartialCostItems,
    int Requests,
    TimeSpan Duration,
    int FailedBatches,
    bool AggregatesFromCache,
    bool QuotesFromCache);

public sealed record ScanResult(
    string World,
    DateTime ScannedUtc,
    IReadOnlyList<SlotCandidate> Candidates,
    ScanStats Stats,
    IReadOnlyList<string> Warnings);
