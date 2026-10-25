using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DesynthLens.Market;

public sealed class AggregatedItem
{
    [JsonPropertyName("itemId")]
    public uint ItemId { get; set; }

    [JsonPropertyName("nq")]
    public AggregatedQuality? Nq { get; set; }
}

public sealed class AggregatedQuality
{
    [JsonPropertyName("minListing")]
    public ListingPrices? MinListing { get; set; }
}

public sealed class ListingPrices
{
    [JsonPropertyName("world")]
    public WorldPrice? World { get; set; }

    [JsonPropertyName("dc")]
    public WorldPrice? Dc { get; set; }
}

public sealed class WorldPrice
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }
}

public sealed class AggregatedResponse
{
    [JsonPropertyName("results")]
    public List<AggregatedItem>? Results { get; set; }
}

/// <summary>Batches of world minimum prices from Universalis, keyed by item id.</summary>
public sealed class MarketPrices : IDisposable
{
    public const int BatchSize = 100;

    private readonly HttpClient http;

    public MarketPrices()
    {
        this.http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("DesynthLens/1.0");
    }

    public async Task<Dictionary<uint, decimal>> FetchAsync(
        string world,
        IReadOnlyList<uint> itemIds,
        Action<int, int>? progress,
        CancellationToken token)
    {
        var prices = new System.Collections.Concurrent.ConcurrentDictionary<uint, decimal>();
        var batches = itemIds.Chunk(BatchSize).ToList();
        var done = 0;

        // Four in flight: Universalis tolerates it and a full scan stops taking a minute.
        using var gate = new SemaphoreSlim(8);

        var tasks = batches.Select(async batch =>
        {
            token.ThrowIfCancellationRequested();
            await gate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                var url = $"https://universalis.app/api/v2/aggregated/{Uri.EscapeDataString(world)}/{string.Join(",", batch)}";
                var body = await this.http.GetStringAsync(url, token).ConfigureAwait(false);
                var response = System.Text.Json.JsonSerializer.Deserialize<AggregatedResponse>(body);

                if (response?.Results is not null)
                {
                    foreach (var entry in response.Results)
                    {
                        // Universalis uses 999,999,999 for "nothing listed on this world", so a
                        // plausible world price wins and the DC price is the fallback.
                        var world = entry.Nq?.MinListing?.World?.Price ?? 0;
                        var dc = entry.Nq?.MinListing?.Dc?.Price ?? 0;

                        var price = IsUsable(world) ? world : IsUsable(dc) ? dc : 0;

                        if (price > 0)
                            prices[entry.ItemId] = price;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // a failed batch just leaves those items unpriced
            }
            finally
            {
                gate.Release();
            }

            var finished = Interlocked.Increment(ref done);
            progress?.Invoke(finished, batches.Count);
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new Dictionary<uint, decimal>(prices);
    }

    /// <summary>Rejects Universalis sentinels and zeroes.</summary>
    private static bool IsUsable(decimal price) => price > 0 && price < 999_999_999m;

    public void Dispose() => this.http.Dispose();
}
