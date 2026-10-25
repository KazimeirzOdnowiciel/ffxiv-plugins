using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SlotYield.Core;

namespace SlotYield.Market;

/// <summary>
/// Two-stage scan.
///
/// Stage 1 asks the cheap aggregated endpoint about every marketable item on the world, one request
/// per batch of 100. It only has DC-wide velocity to work with, so it is used to rank, not to decide.
///
/// Stage 2 pulls real listings and per-world sales history for the shortlist, which is where the
/// exact per-quality velocity, the sellers at your price and the outlier-trimmed minimum come from.
/// </summary>
public sealed class MarketScanner
{
    private const int QuoteBatchSize = 20;
    private const int ListingsPerQuote = 60;
    private const int HistoryPerQuote = 120;

    private readonly UniversalisClient client;
    private readonly MarketCache cache;
    private readonly IRecipeSource? recipes;
    private readonly Func<uint, decimal?>? vendorPrice;

    public MarketScanner(
        UniversalisClient client,
        MarketCache cache,
        IRecipeSource? recipes = null,
        Func<uint, decimal?>? vendorPrice = null)
    {
        this.client = client;
        this.cache = cache;
        this.recipes = recipes;
        this.vendorPrice = vendorPrice;
    }

    public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var startRequests = this.client.RequestCount;

        var marketable = await this.GetMarketableAsync(options, token).ConfigureAwait(false);
        progress?.Report(new ScanProgress("market", 1, 1, $"{marketable.Count} marketable items"));

        var (aggregates, aggregatesFromCache) = await this
            .GetAggregatesAsync(options, marketable, progress, token)
            .ConfigureAwait(false);

        var shortlist = aggregates.Items
            .SelectMany(row => row.ToPreliminary(options.Model))
            .OrderByDescending(p => p.Value)
            .Take(options.ShortlistSize)
            .Select(p => p.ItemId)
            .Concat(options.ExtraItemIds ?? Array.Empty<uint>())
            .Distinct()
            .ToList();

        var (quotes, quotesFromCache, quoteFailures) = await this
            .GetQuotesAsync(options, shortlist, progress, token)
            .ConfigureAwait(false);

        var calculator = options.CostMode != CostMode.Revenue && this.recipes is not null
            ? new CostCalculator(
                this.recipes,
                new CompositePriceSource(quotes, aggregates.Items, this.vendorPrice),
                options.CostMode)
            : null;

        var candidates = quotes
            .Select(quote =>
            {
                var score = SlotModel.Score(quote, options.Model);
                var cost = calculator?.Compute(quote.ItemId, options.Model.StackSize);
                score = SlotModel.ApplyCost(score, cost, expectCost: calculator is not null, stackSize: options.Model.StackSize);
                return new SlotCandidate(quote.ItemId, quote.Quality, quote, score, cost);
            })
            .ToList();

        candidates.Sort((a, b) =>
        {
            var byValue = b.Score.ProfitPerDay.CompareTo(a.Score.ProfitPerDay);
            return byValue != 0 ? byValue : a.Quote.ItemId.CompareTo(b.Quote.ItemId);
        });

        stopwatch.Stop();

        var warnings = new List<string>();
        if (aggregates.FailedBatches > 0)
            warnings.Add($"{aggregates.FailedBatches} market batches failed and were skipped.");

        if (quoteFailures > 0)
            warnings.Add($"{quoteFailures} price batches failed; some items are missing prices this scan.");

        var staleCount = candidates.Count(c => c.Score.IsStale);
        if (staleCount > 0)
        {
            warnings.Add(
                $"{staleCount} of {candidates.Count} priced items have market data older than " +
                $"{SlotModel.StaleAfterMinutes:0} minutes; the 'fresh only' filter hides them.");
        }

        if (candidates.Count == 0)
            warnings.Add("No priced items. Check the world name, or raise the shortlist size.");

        var costed = candidates.Count(c => c.Score.CostPerUnit is not null);
        var craftCosted = candidates.Count(c => c.Score.CostSource == "craft");
        var marketCosted = candidates.Count(c => c.Score.CostSource.StartsWith("market", StringComparison.Ordinal));
        var partial = candidates.Count(c => c.Score.CostIsPartial);

        return new ScanResult(
            options.World,
            DateTime.UtcNow,
            candidates,
            new ScanStats(
                marketable.Count,
                aggregates.Items.Count,
                candidates.Count,
                costed,
                craftCosted,
                marketCosted,
                partial,
                this.client.RequestCount - startRequests,
                stopwatch.Elapsed,
                aggregates.FailedBatches,
                aggregatesFromCache,
                quotesFromCache),
            warnings);
    }

    private async Task<List<uint>> GetMarketableAsync(ScanOptions options, CancellationToken token)
    {
        if (options.UseCache
            && this.cache.TryLoad<List<uint>>("marketable", TimeSpan.FromHours(24), out var cached)
            && cached is { Count: > 0 })
        {
            return cached;
        }

        var fetched = await this.client.GetMarketableAsync(token).ConfigureAwait(false);
        if (fetched is null || fetched.Length == 0)
            throw new MarketRequestException("Universalis returned no marketable items.", null);

        var list = fetched.ToList();
        this.cache.Save("marketable", list);
        return list;
    }

    private async Task<(AggregateSet Set, bool FromCache)> GetAggregatesAsync(
        ScanOptions options,
        List<uint> marketable,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        var key = $"agg-{options.World}";

        if (options.UseCache
            && this.cache.TryLoad<AggregateSet>(key, options.CacheMaxAge, out var cachedSet)
            && cachedSet is { Items.Count: > 0 })
        {
            progress?.Report(new ScanProgress(
                "aggregate",
                1,
                1,
                $"cached scan from {cachedSet.ScannedUtc.ToLocalTime():g}"));
            return (cachedSet, true);
        }

        var batches = Chunk(marketable, options.BatchSize);
        var set = new AggregateSet { World = options.World, ScannedUtc = DateTime.UtcNow };
        var done = 0;

        progress?.Report(new ScanProgress("aggregate", 0, batches.Count, "scanning the market board..."));

        var responses = await Task.WhenAll(batches.Select(batch => this.SafeAggregateAsync(options.World, batch, token)))
            .ConfigureAwait(false);

        foreach (var response in responses)
        {
            done++;
            if (response is null)
            {
                set.FailedBatches++;
            }
            else if (response.Results is not null)
            {
                foreach (var item in response.Results)
                {
                    var row = AggregateRow.From(item);
                    if (row is not null)
                        set.Items.Add(row);
                }
            }

            progress?.Report(new ScanProgress("aggregate", done, batches.Count, $"{set.Items.Count} items"));
        }

        if (set.Items.Count == 0)
            throw new MarketRequestException("Every aggregated request failed.", null);

        // A scan with missing batches is used for this run but not cached: caching it would hide the
        // missing items for the whole cache window and make the next scan look complete when it isn't.
        if (set.FailedBatches == 0)
            this.cache.Save(key, set);

        return (set, false);
    }

    private async Task<(List<MarketQuote> Quotes, bool FromCache, int Failed)> GetQuotesAsync(
        ScanOptions options,
        List<uint> shortlist,
        IProgress<ScanProgress>? progress,
        CancellationToken token)
    {
        var key = $"quotes-{options.World}";
        var quotes = new Dictionary<(uint ItemId, Quality Quality), MarketQuote>();

        if (options.UseCache
            && this.cache.TryLoad<List<MarketQuote>>(key, options.CacheMaxAge, out var cached)
            && cached is not null)
        {
            foreach (var quote in cached)
                quotes[(quote.ItemId, quote.Quality)] = quote;
        }

        var wanted = new HashSet<uint>(shortlist);

        // Items that had no listings at all last time are remembered, so every scan does not pay a
        // request for the same twenty dead listings. The memory expires with the rest of the cache.
        var emptyKey = $"quotes-empty-{options.World}";
        var knownEmpty = new HashSet<uint>();
        if (options.UseCache
            && this.cache.TryLoad<List<uint>>(emptyKey, options.CacheMaxAge, out var cachedEmpty)
            && cachedEmpty is not null)
        {
            knownEmpty.UnionWith(cachedEmpty);
        }

        var missing = shortlist
            .Where(id => !knownEmpty.Contains(id))
            .Where(id => !quotes.ContainsKey((id, Quality.Nq)) && !quotes.ContainsKey((id, Quality.Hq)))
            .ToList();

        if (missing.Count == 0)
        {
            progress?.Report(new ScanProgress("pricing", 1, 1, "prices are cached"));
            return (quotes.Values.Where(q => wanted.Contains(q.ItemId)).ToList(), true, 0);
        }

        var batches = Chunk(missing, QuoteBatchSize);
        var done = 0;
        var failed = 0;
        var fetchedAt = DateTime.UtcNow;

        progress?.Report(new ScanProgress("pricing", 0, batches.Count, "pricing the shortlist..."));

        var responses = await Task.WhenAll(batches.Select(batch =>
                this.SafeQuotesAsync(options.World, batch, token)))
            .ConfigureAwait(false);

        foreach (var response in responses)
        {
            done++;

            if (response?.Items is null)
            {
                failed++;
            }
            else
            {
                foreach (var entry in response.Items.Values)
                {
                    var nq = QuoteFactory.Build(entry, Quality.Nq, fetchedAt);
                    if (nq is not null)
                        quotes[(nq.ItemId, Quality.Nq)] = nq;

                    var hq = QuoteFactory.Build(entry, Quality.Hq, fetchedAt);
                    if (hq is not null)
                        quotes[(hq.ItemId, Quality.Hq)] = hq;
                }
            }

            progress?.Report(new ScanProgress("pricing", done, batches.Count, $"{quotes.Count} quotes"));
        }

        var result = quotes.Values.Where(q => wanted.Contains(q.ItemId)).ToList();
        this.cache.Save(key, result);

        var stillEmpty = shortlist
            .Where(id => !quotes.ContainsKey((id, Quality.Nq)) && !quotes.ContainsKey((id, Quality.Hq)))
            .ToList();

        this.cache.Save(emptyKey, stillEmpty);
        return (result, false, failed);
    }

    private async Task<AggregatedResponse?> SafeAggregateAsync(string world, List<uint> batch, CancellationToken token)
    {
        try
        {
            return await this.client.GetAggregatedAsync(world, batch, token).ConfigureAwait(false);
        }
        catch (MarketRequestException)
        {
            return null;
        }
    }

    private async Task<QuotesResponse?> SafeQuotesAsync(string world, List<uint> batch, CancellationToken token)
    {
        try
        {
            return await this.client.GetQuotesAsync(world, batch, ListingsPerQuote, HistoryPerQuote, token)
                .ConfigureAwait(false);
        }
        catch (MarketRequestException)
        {
            return null;
        }
    }

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var chunks = new List<List<T>>((source.Count + size - 1) / size);
        for (var i = 0; i < source.Count; i += size)
            chunks.Add(source.GetRange(i, Math.Min(size, source.Count - i)));

        return chunks;
    }
}

public sealed class AggregateSet
{
    public string World { get; set; } = string.Empty;

    public DateTime ScannedUtc { get; set; }

    public int FailedBatches { get; set; }

    public List<AggregateRow> Items { get; set; } = new();
}

public sealed class AggregateRow
{
    public uint ItemId { get; set; }

    public decimal NqMin { get; set; }

    public decimal HqMin { get; set; }

    public double NqVelocity { get; set; }

    public double HqVelocity { get; set; }

    public decimal NqAverage { get; set; }

    public decimal HqAverage { get; set; }

    public static AggregateRow? From(AggregatedItem item)
    {
        // Batch requests omit the world-scoped minimum, so fall back to the DC minimum.
        var nqPrice = item.Nq?.MinListing?.World?.Price ?? item.Nq?.MinListing?.Dc?.Price ?? 0m;
        var hqPrice = item.Hq?.MinListing?.World?.Price ?? item.Hq?.MinListing?.Dc?.Price ?? 0m;

        if (nqPrice <= 0m && hqPrice <= 0m)
            return null;

        return new AggregateRow
        {
            ItemId = item.ItemId,
            NqMin = nqPrice,
            HqMin = hqPrice,
            NqVelocity = item.Nq?.DailySaleVelocity?.Dc?.Quantity ?? 0,
            HqVelocity = item.Hq?.DailySaleVelocity?.Dc?.Quantity ?? 0,
            NqAverage = item.Nq?.AverageSalePrice?.Dc?.Price ?? 0m,
            HqAverage = item.Hq?.AverageSalePrice?.Dc?.Price ?? 0m,
        };
    }

    /// <summary>
    /// Stage 1 ranking heuristic only: "how big is this market across the DC". The real per-world
    /// numbers are computed in stage 2 from listings and history.
    /// </summary>
    public IEnumerable<(uint ItemId, decimal Value, Quality Quality)> ToPreliminary(SlotParameters parameters)
    {
        var net = 1m - parameters.TaxRate;

        var nqPrice = this.NqAverage > 0m ? this.NqAverage : this.NqMin;
        if (nqPrice > 0m && this.NqVelocity > 0)
            yield return (this.ItemId, nqPrice * (decimal)this.NqVelocity * net, Quality.Nq);

        var hqPrice = this.HqAverage > 0m ? this.HqAverage : this.HqMin;
        if (hqPrice > 0m && this.HqVelocity > 0)
            yield return (this.ItemId, hqPrice * (decimal)this.HqVelocity * net, Quality.Hq);
    }
}
