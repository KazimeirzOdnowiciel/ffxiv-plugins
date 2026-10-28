using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SlotYield.Core;

namespace SlotYield.Market;

/// <summary>One buy-here / sell-there opportunity discovered across worlds.</summary>
public sealed record ArbitrageRow(
    uint ItemId,
    Quality Quality,
    string BuyWorld,
    decimal BuyPrice,
    string SellWorld,
    decimal SellPrice,
    decimal NetPerUnit,
    double SellVelocity,
    string HomeWorld,
    decimal HomePrice);

public sealed record ArbitrageReport(
    DateTime ComputedUtc,
    IReadOnlyList<string> Worlds,
    IReadOnlyList<ArbitrageRow> Rows,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Cross-world arbitrage: for the scan's best items, asks Universalis for every world in the
/// chosen set, then reports the cheapest world to buy on against the priciest world to sell on.
/// </summary>
public sealed class ArbitrageService
{
    private readonly UniversalisClient client;

    public ArbitrageService(UniversalisClient client) => this.client = client;

    public async Task<ArbitrageReport> ComputeAsync(
        ScanResult result,
        IReadOnlyList<string> worlds,
        int topItems,
        decimal tax,
        CancellationToken token)
    {
        var warnings = new List<string>();

        var items = result.Candidates
            .Where(c => !c.Score.IsDead && !c.Score.IsSuspect)
            .OrderByDescending(c => c.Score.ProfitPerDay)
            .Take(Math.Clamp(topItems, 10, 400))
            .ToList();

        var ids = items.Select(c => c.ItemId).Distinct().ToList();
        if (ids.Count == 0 || worlds.Count == 0)
            return new ArbitrageReport(DateTime.UtcNow, worlds, Array.Empty<ArbitrageRow>(), warnings);

        var home = result.World;
        var queryWorlds = worlds.Any(world => world.Equals(home, StringComparison.OrdinalIgnoreCase))
            ? worlds
            : worlds.Append(home).ToList();

        // world -> item id -> the four numbers we care about, per quality.
        var data = new Dictionary<string, Dictionary<uint, WorldSample>>(StringComparer.OrdinalIgnoreCase);

        foreach (var world in queryWorlds)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var response = await this.client.GetAggregatedAsync(world, ids, token).ConfigureAwait(false);
                var samples = new Dictionary<uint, WorldSample>();
                foreach (var item in response?.Results ?? new List<AggregatedItem>())
                {
                    samples[item.ItemId] = new WorldSample(
                        item.Nq?.MinListing?.World?.Price ?? 0m,
                        item.Nq?.AverageSalePrice?.World?.Price ?? 0m,
                        item.Nq?.DailySaleVelocity?.World?.Quantity ?? 0d,
                        item.Hq?.MinListing?.World?.Price ?? 0m,
                        item.Hq?.AverageSalePrice?.World?.Price ?? 0m,
                        item.Hq?.DailySaleVelocity?.World?.Quantity ?? 0d);
                }

                data[world] = samples;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"{world}: {exception.Message}");
            }
        }

        var rows = new List<ArbitrageRow>();
        var rate = 1m - Math.Clamp(tax, 0m, 0.2m);

        foreach (var group in items.GroupBy(c => c.ItemId))
        {
            foreach (var candidate in group)
            {
                var hq = candidate.Quality == Quality.Hq;
                var buyWorld = string.Empty;
                var buyPrice = decimal.MaxValue;
                var sellWorld = string.Empty;
                var sellPrice = 0m;
                var sellVelocity = 0d;

                foreach (var world in worlds)
                {
                    if (!data.TryGetValue(world, out var samples) || !samples.TryGetValue(candidate.ItemId, out var sample))
                        continue;

                    var candidateBuy = hq ? sample.HqBuy : sample.NqBuy;
                    var candidateSell = hq ? sample.HqSell : sample.NqSell;
                    var candidateVelocity = hq ? sample.HqVelocity : sample.NqVelocity;

                    if (candidateBuy > 0m && candidateBuy < buyPrice)
                    {
                        buyPrice = candidateBuy;
                        buyWorld = world;
                    }

                    if (candidateVelocity >= 0.1 && candidateSell > sellPrice)
                    {
                        sellPrice = candidateSell;
                        sellWorld = world;
                        sellVelocity = candidateVelocity;
                    }
                }

                if (buyWorld.Length == 0 || sellWorld.Length == 0 || buyWorld.Equals(sellWorld, StringComparison.OrdinalIgnoreCase))
                    continue;

                var net = Math.Round(sellPrice * rate, 0, MidpointRounding.AwayFromZero) - buyPrice;
                if (net <= 0m)
                    continue;

                var homePrice = 0m;
                if (data.TryGetValue(home, out var homeSamples) && homeSamples.TryGetValue(candidate.ItemId, out var homeSample))
                    homePrice = hq ? homeSample.HqBuy : homeSample.NqBuy;

                rows.Add(new ArbitrageRow(
                    candidate.ItemId,
                    candidate.Quality,
                    buyWorld,
                    buyPrice,
                    sellWorld,
                    sellPrice,
                    net,
                    sellVelocity,
                    home,
                    homePrice));
            }
        }

        rows.Sort((left, right) => right.NetPerUnit.CompareTo(left.NetPerUnit));
        return new ArbitrageReport(DateTime.UtcNow, worlds, rows, warnings);
    }

    private readonly record struct WorldSample(
        decimal NqBuy,
        decimal NqSell,
        double NqVelocity,
        decimal HqBuy,
        decimal HqSell,
        double HqVelocity);
}
