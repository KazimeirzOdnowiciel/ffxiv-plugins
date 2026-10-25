using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

/// <summary>One line of a saved scan: enough to notice movement next time.</summary>
public sealed record SnapshotEntry(
    uint ItemId,
    Quality Quality,
    decimal Price,
    decimal ProfitPerDay,
    double DaysToClear);

public sealed record ScanSnapshot(DateTime TakenUtc, string World, IReadOnlyList<SnapshotEntry> Entries)
{
    public static ScanSnapshot Empty { get; } = new(DateTime.MinValue, string.Empty, Array.Empty<SnapshotEntry>());

    /// <summary>Cost basis used when the snapshot was taken; profit deltas are only comparable within one.</summary>
    public CostMode Mode { get; init; } = CostMode.Revenue;
}

public sealed record EntryDelta(SnapshotEntry Previous, SnapshotEntry Current)
{
    public uint ItemId => this.Current.ItemId;

    public Quality Quality => this.Current.Quality;

    public decimal PriceChange => this.Current.Price - this.Previous.Price;

    public double PriceChangePercent =>
        this.Previous.Price <= 0m ? 0 : (double)(this.PriceChange / this.Previous.Price);

    public decimal ProfitChange => this.Current.ProfitPerDay - this.Previous.ProfitPerDay;
}

public sealed record ScanDiff(
    DateTime PreviousUtc,
    int PreviousCount,
    int CurrentCount,
    IReadOnlyList<EntryDelta> Drops,
    IReadOnlyList<EntryDelta> Rises,
    IReadOnlyList<SnapshotEntry> NewEntries,
    IReadOnlyList<SnapshotEntry> Gone)
{
    public static ScanDiff Empty { get; } = new(
        DateTime.MinValue,
        0,
        0,
        Array.Empty<EntryDelta>(),
        Array.Empty<EntryDelta>(),
        Array.Empty<SnapshotEntry>(),
        Array.Empty<SnapshotEntry>());

    public bool HasAnything => this.Drops.Count > 0 || this.Rises.Count > 0 || this.NewEntries.Count > 0 || this.Gone.Count > 0;
}

/// <summary>
/// Chooses what a snapshot remembers: the strongest <paramref name="top"/> items plus anything pinned,
/// because your own slots deserve a diff even when they are not in the top 150 that day.
/// </summary>
public static class SnapshotBuilder
{
    public static IReadOnlyList<SnapshotEntry> Select(
        IEnumerable<SnapshotEntry> ranked,
        IReadOnlySet<(uint ItemId, Quality Quality)> pinned,
        int top = 150)
    {
        var source = ranked as IReadOnlyList<SnapshotEntry> ?? ranked.ToList();
        var result = new List<SnapshotEntry>();
        var seen = new HashSet<(uint ItemId, Quality Quality)>();

        foreach (var entry in source.OrderByDescending(e => e.ProfitPerDay).Take(Math.Max(0, top)))
        {
            if (seen.Add((entry.ItemId, entry.Quality)))
                result.Add(entry);
        }

        if (pinned.Count > 0)
        {
            foreach (var entry in source)
            {
                var key = (entry.ItemId, entry.Quality);
                if (pinned.Contains(key) && seen.Add(key))
                    result.Add(entry);
            }
        }

        return result;
    }
}

/// <summary>
/// Compares two scans. Only items present in both can have a price change; the rest are arrivals
/// and departures from the tracked list. Small moves are ignored, because a market that wobbles by
/// 2% has not told you anything.
/// </summary>
public static class SnapshotDiff
{
    /// <summary>Changes smaller than this are noise.</summary>
    public const double MinPercentToReport = 0.05;

    public static ScanDiff Compare(ScanSnapshot previous, ScanSnapshot current, int take = 10)
    {
        if (previous.Entries.Count == 0 || current.Entries.Count == 0)
        {
            return new ScanDiff(
                previous.TakenUtc,
                previous.Entries.Count,
                current.Entries.Count,
                Array.Empty<EntryDelta>(),
                Array.Empty<EntryDelta>(),
                current.Entries.Take(take).ToList(),
                previous.Entries.Take(take).ToList());
        }

        var previousByKey = new Dictionary<(uint, Quality), SnapshotEntry>();
        foreach (var entry in previous.Entries)
            previousByKey.TryAdd((entry.ItemId, entry.Quality), entry);

        var currentByKey = new Dictionary<(uint, Quality), SnapshotEntry>();
        foreach (var entry in current.Entries)
            currentByKey.TryAdd((entry.ItemId, entry.Quality), entry);

        var common = current.Entries
            .Where(entry => previousByKey.ContainsKey((entry.ItemId, entry.Quality)))
            .Select(entry => new EntryDelta(previousByKey[(entry.ItemId, entry.Quality)], entry))
            .ToList();

        var drops = common
            .Where(delta => delta.PriceChangePercent <= -MinPercentToReport)
            .OrderBy(delta => delta.PriceChangePercent)
            .Take(take)
            .ToList();

        var rises = common
            .Where(delta => delta.PriceChangePercent >= MinPercentToReport)
            .OrderByDescending(delta => delta.PriceChangePercent)
            .Take(take)
            .ToList();

        var newEntries = current.Entries
            .Where(entry => !previousByKey.ContainsKey((entry.ItemId, entry.Quality)))
            .OrderByDescending(entry => entry.ProfitPerDay)
            .Take(take)
            .ToList();

        var gone = previous.Entries
            .Where(entry => !currentByKey.ContainsKey((entry.ItemId, entry.Quality)))
            .OrderByDescending(entry => entry.ProfitPerDay)
            .Take(take)
            .ToList();

        return new ScanDiff(previous.TakenUtc, previous.Entries.Count, current.Entries.Count, drops, rises, newEntries, gone);
    }
}
