using System.Text.Json;
using SlotYield.Core;
using SlotYield.Market;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void Section(string name) => Console.WriteLine($"\n== {name}");

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
};

var dir = AppContext.BaseDirectory;
var aggregatedJson = File.ReadAllText(Path.Combine(dir, "aggregated-light.json"));
var quotesJson = File.ReadAllText(Path.Combine(dir, "quotes-light.json"));

// ------------------------------------------------------------- parsing ----
Section("aggregated parsing");

var aggregated = JsonSerializer.Deserialize<AggregatedResponse>(aggregatedJson, options);
Check(aggregated?.Results is { Count: >= 3 }, "aggregated fixture should contain at least three items");

var item = aggregated!.Results!.First(r => r.ItemId == 5333);
Console.WriteLine(
    $"  5333: dc min={item.Nq?.MinListing?.Dc?.Price} dc velocity={item.Nq?.DailySaleVelocity?.Dc?.Quantity:0.0} dc avg={item.Nq?.AverageSalePrice?.Dc?.Price:0.0}");

Check(item.Nq?.MinListing?.Dc?.Price > 0, "the batch endpoint reports DC-level minimums");
Check(item.Nq?.DailySaleVelocity?.Dc?.Quantity > 0, "DC velocity should be present");
Check(item.Nq?.AverageSalePrice?.Dc?.Price > 0, "DC average sale price should be present");

var row = AggregateRow.From(item);
Check(row is not null, "an item with a price should produce a row");
Check(row!.NqMin > 0 && row.NqVelocity > 0, "row should carry price and velocity");

var prelim = row.ToPreliminary(new SlotParameters { TaxRate = 0.03m }).ToList();
Check(prelim.Count >= 1, "row should yield at least one preliminary candidate");
Check(prelim.All(p => p.Value > 0), "preliminary values should be positive");
Check(AggregateRow.From(new AggregatedItem { ItemId = 7 }) is null, "an item with no prices makes no row");

// --------------------------------------------------------------- quotes ----
Section("quotes parsing");

var quotes = JsonSerializer.Deserialize<QuotesResponse>(quotesJson, options);
Check(quotes?.Items is { Count: >= 3 }, "quotes fixture should contain at least three items");

var quoted = quotes!.Items!["5333"];
Check(quoted.Listings is { Count: > 0 }, "quoted item should have listings");
Check(quoted.RecentHistory is { Count: > 0 }, "quoted item should have history");

var nq = QuoteFactory.Build(quoted, Quality.Nq, DateTime.UtcNow);
Check(nq is not null, "NQ quote should build from a market with NQ listings");
Console.WriteLine(
    $"  NQ: min={nq!.MinListing} avg={nq.AverageSale:0} velocity={nq.DailyVelocity:0.00}/day sellers={nq.DistinctSellers} atMin={nq.SellersAtMinPrice} samples={nq.HistorySamples} span={nq.HistorySpanDays:0.0}d trend={nq.TrendPercent:P0}");

Check(nq.MinListing > 0, "min listing should be positive");
Check(nq.DailyVelocity > 0, "velocity should be positive for a live item");
Check(nq.DistinctSellers >= 1, "there must be at least one seller");
Check(nq.SellersAtMinPrice >= 1, "at least one seller is at the minimum");
Check(nq.SellersAtMinPrice <= nq.DistinctSellers, "sellers at min cannot exceed all sellers");
Check(nq.ListingCount > 0, "listing count should be positive");
Check(nq.AverageSale >= nq.MinListing * 0.5m && nq.AverageSale <= nq.MinListing * 10m, "average sale must be clamped near the board");
Check(nq.LastUploadUtc is not null, "upload time should be parsed");

    Check(nq.CheapestSteps.Length is > 0 and <= 5, $"the ladder has 1-5 rungs, got {nq.CheapestSteps.Length}");
    Check(nq.CheapestSteps.Length == 0 || nq.CheapestSteps[0].Price == nq.MinListing, "the first rung is the minimum");
    Check(nq.CheapestSteps.SequenceEqual(nq.CheapestSteps.OrderBy(step => step.Price)), "rungs are ordered cheapest first");
    Check(nq.CheapestSteps.All(step => step.Listings > 0), "every rung counts at least one listing");
    Check(nq.CheapestSteps.All(step => step.Price >= nq.MinListing), "no rung sits below the minimum");
Check(nq.HistorySamples > 0 && nq.HistorySpanDays > 0, "history should carry samples and a span");
Check(nq.PriceHistory.Length is 0 or 30, "a price series is either absent or thirty days");
Check(nq.PriceHistory.Length == 0 || nq.PriceHistory.All(p => p > 0m), "price series points must be positive");

var hq = QuoteFactory.Build(quoted, Quality.Hq, DateTime.UtcNow);
Check(hq is not null, "HQ quote should build when HQ listings exist");
Check(hq!.Quality == Quality.Hq, "HQ quote must be flagged HQ");
Check(hq.MinListing >= 0, "HQ min listing should be sane");

// No listings for a quality means no quote.
var nqOnly = new QuotedItem
{
    ItemId = 99,
    Listings = new List<Listing> { new() { PricePerUnit = 100m, Quantity = 5, Hq = false, RetainerName = "A" } },
    RecentHistory = new List<Sale>(),
};
Check(QuoteFactory.Build(nqOnly, Quality.Hq, DateTime.UtcNow) is null, "an item with no HQ listings has no HQ quote");

// ------------------------------------------------------- outlier trimming ----
Section("outlier trimming");

var polluted = new List<Listing>
{
    new() { PricePerUnit = 1m, Quantity = 1, RetainerName = "troll" },
    new() { PricePerUnit = 20_000m, Quantity = 10, RetainerName = "a" },
    new() { PricePerUnit = 20_000m, Quantity = 10, RetainerName = "b" },
    new() { PricePerUnit = 21_000m, Quantity = 10, RetainerName = "c" },
};

var trimmed = QuoteFactory.TrimOutliers(polluted);
Console.WriteLine($"  {polluted.Count} listings -> {trimmed.Count} after trimming, min={trimmed.Min(l => l.PricePerUnit)}");
Check(trimmed.Count == 3, "the 1-gil phantom should be dropped");
Check(trimmed.Min(l => l.PricePerUnit) == 20_000m, "the real minimum should survive the trim");

var honest = new List<Listing>
{
    new() { PricePerUnit = 5m, Quantity = 10, RetainerName = "a" },
    new() { PricePerUnit = 6m, Quantity = 10, RetainerName = "b" },
    new() { PricePerUnit = 7m, Quantity = 10, RetainerName = "c" },
};
Check(QuoteFactory.TrimOutliers(honest).Count == 3, "a normal cheap market must not be trimmed");

// ---------------------------------------------------------------- velocity ----
Section("velocity");

var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var day = 86_400L;

var twoSalesOverTenDays = new List<Sale>
{
    new() { PricePerUnit = 100m, Quantity = 1, Timestamp = now - (9 * day) },
    new() { PricePerUnit = 100m, Quantity = 1, Timestamp = now - (1 * day) },
};

var measured = QuoteFactory.Measure(twoSalesOverTenDays, now);
Console.WriteLine($"  two sales, oldest 9d ago: velocity={measured.Velocity:0.000}/day span={measured.SpanDays:0.0}d");
Check(Math.Abs(measured.SpanDays - 9) < 0.01, $"span should be 9 days, got {measured.SpanDays}");
Check(Math.Abs(measured.Velocity - (2.0 / 9.0)) < 0.01, $"velocity should be 0.22/day, got {measured.Velocity}");

var singleSale = QuoteFactory.Measure(
    new List<Sale> { new() { Quantity = 1, PricePerUnit = 1m, Timestamp = now - (29 * day) } },
    now);
Check(singleSale.Velocity < 0.05, "one sale a month ago must not read as one sale per day");

var burstMeasure = QuoteFactory.Measure(
    Enumerable.Range(0, 50).Select(i => new Sale { Quantity = 2, PricePerUnit = 100m, Timestamp = now - (i * 3600) }).ToList(),
    now);
Check(burstMeasure.Velocity > 1.5, $"100 units in ~2 days is a high velocity, got {burstMeasure.Velocity:0.0}");
Check(burstMeasure.SpanDays <= QuoteFactory.MaxWindowDays, "span must respect the window cap");

// ---------------------------------------------------------------- average ----
Section("average sale price");

var weighted = QuoteFactory.WeightedAveragePrice(
    new List<Sale>
    {
        new() { PricePerUnit = 100m, Quantity = 1 },
        new() { PricePerUnit = 200m, Quantity = 9 },
    },
    100m);
Check(weighted == 190m, $"quantity-weighted average should be 190, got {weighted}");

var absurd = QuoteFactory.WeightedAveragePrice(
    new List<Sale> { new() { PricePerUnit = 1_000_000m, Quantity = 1 } },
    100m);
Check(absurd == 1000m, $"an absurd sale must clamp to 10x the listing, got {absurd}");

// -------------------------------------------------------------- wash trades ----
Section("wash trade detection");

var bursty = new List<Sale>();
for (var i = 0; i < 8; i++)
    bursty.Add(new Sale { PricePerUnit = 3_199_994m - i, Quantity = 1, Timestamp = now - (600 - (i * 20)) });

var spreadOut = new List<Sale>();
for (var i = 0; i < 8; i++)
    spreadOut.Add(new Sale { PricePerUnit = 3_199_994m - i, Quantity = 1, Timestamp = now - (i * 7200) });

var cheapBurst = new List<Sale>();
for (var i = 0; i < 8; i++)
    cheapBurst.Add(new Sale { PricePerUnit = 100m, Quantity = 1, Timestamp = now - (600 - (i * 20)) });

var oneMinuteApart = QuoteFactory.WashTradeShare(bursty);
var hoursApart = QuoteFactory.WashTradeShare(spreadOut);
var cheap = QuoteFactory.WashTradeShare(cheapBurst);

Console.WriteLine($"  burst={oneMinuteApart:P0} spread={hoursApart:P0} cheap={cheap:P0}");
Check(oneMinuteApart >= 0.5, $"a burst of expensive one-unit sales should look manufactured, got {oneMinuteApart:P0}");
Check(hoursApart == 0, $"sales hours apart are normal, got {hoursApart:P0}");
Check(cheap == 0, "cheap commodities selling in bursts are normal");

// ------------------------------------------------------------ price source ----
Section("material price source");

var quotePrices = new[]
{
    new MarketQuote { ItemId = 10, Quality = Quality.Nq, MinListing = 500m },
    new MarketQuote { ItemId = 10, Quality = Quality.Hq, MinListing = 900m },
};

var aggregatePrices = new[]
{
    new AggregateRow { ItemId = 11, NqAverage = 300m, NqMin = 250m },
    new AggregateRow { ItemId = 12, NqAverage = 0m, NqMin = 200m },
    new AggregateRow { ItemId = 14, NqAverage = 0m, NqMin = 0m, HqAverage = 800m },
};

var priceSource = new CompositePriceSource(quotePrices, aggregatePrices, id => id == 13 ? 99m : null);

Check(priceSource.UnitPrice(10) == 500m, "world quotes win, and NQ is preferred");
Check(priceSource.UnitPrice(11) == 300m, "aggregate average is the next source");
Check(priceSource.UnitPrice(12) == 200m, "aggregate minimum is used when there is no average");
Check(priceSource.UnitPrice(13) == 99m, "vendor price is the last resort");
Check(priceSource.UnitPrice(14) == 800m, "HQ average is used when nothing NQ exists");
Check(priceSource.UnitPrice(15) is null, "unknown items have no price");

// ------------------------------------------------------------ price history ----
Section("price history");

var histDay = 86_400L;

var flat = new List<SalePoint>();
for (var d = 0; d < 30; d++)
    flat.Add(new SalePoint(now - (d * histDay), 100m, 3));

var flatSeries = PriceHistory.Build(flat, now);
Check(flatSeries.Length == 30, $"a month of sales makes a 30 point series, got {flatSeries.Length}");
Check(Math.Abs(PriceHistory.TrendPercent(flatSeries)) < 0.01, "flat prices have no trend");

var rising = new List<SalePoint>();
for (var d = 0; d < 30; d++)
    rising.Add(new SalePoint(now - (d * histDay), 100m + ((29 - d) * (100m / 29m)), 2));

var risingSeries = PriceHistory.Build(rising, now);
var risingTrend = PriceHistory.TrendPercent(risingSeries);
Console.WriteLine($"  rising: {Money.Gil(risingSeries[0])} -> {Money.Gil(risingSeries[^1])}, trend {risingTrend:P0}");
Check(risingTrend > 0.3, $"prices doubling should trend up, got {risingTrend:P0}");

var falling = rising.Select(p => new SalePoint(p.Timestamp, 300m - p.Price, p.Quantity)).ToList();
var fallingTrend = PriceHistory.TrendPercent(PriceHistory.Build(falling, now));
Console.WriteLine($"  falling: trend {fallingTrend:P0}");
Check(fallingTrend < -0.3, $"prices halving should trend down, got {fallingTrend:P0}");

var sparse = new List<SalePoint>
{
    new(now - (2 * histDay), 500m, 1),
    new(now - (10 * histDay), 450m, 1),
    new(now - (20 * histDay), 400m, 1),
};
var sparseSeries = PriceHistory.Build(sparse, now);
Check(sparseSeries.Length == 30, "a sparse series is still 30 points");
Check(sparseSeries.All(v => v > 0m), "missing days carry a price forward, never zero");

var tooSparse = new List<SalePoint>
{
    new(now - (2 * histDay), 500m, 1),
    new(now - (20 * histDay), 400m, 1),
};
Check(PriceHistory.Build(tooSparse, now).Length == 0, "two days of sales is not a trend");

var outlierSales = new List<SalePoint>();
for (var i = 0; i < 20; i++)
    outlierSales.Add(new SalePoint(now - ((i * histDay) / 2), 1000m, 1));
outlierSales.Add(new SalePoint(now - (2 * histDay), 1m, 1));
outlierSales.Add(new SalePoint(now - (3 * histDay), 1m, 1));

var outlierSeries = PriceHistory.Build(outlierSales, now);
Console.WriteLine($"  trimmed min={(outlierSeries.Length > 0 ? Money.Gil(outlierSeries.Min()) : "n/a")}");
Check(outlierSeries.Length == 0 || outlierSeries.All(v => v > 900m), "1-gil outliers are trimmed from the series");

// ------------------------------------------------------------------ cache ----
Section("cache");

var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache-test-" + Guid.NewGuid().ToString("N"));
var cache = new MarketCache(cacheDir);
var payload = new List<uint> { 1, 2, 3 };
cache.Save("sample", payload);

Check(cache.TryLoad<List<uint>>("sample", TimeSpan.FromHours(1), out var loaded), "a fresh entry should load");
Check(loaded is { Count: 3 } && loaded[2] == 3, "the payload should round-trip");
Check(!cache.TryLoad<List<uint>>("sample", TimeSpan.Zero, out _), "an entry older than the TTL should not load");
Check(!cache.TryLoad<List<uint>>("missing", TimeSpan.FromHours(1), out _), "a missing key should not load");

// A file written by an older schema is discarded, not misread.
File.WriteAllText(
    Path.Combine(cacheDir, "cache-old.json"),
    "{\"schema\":1,\"savedUtc\":\"2026-01-01T00:00:00Z\",\"payload\":[1,2]}");
Check(!cache.TryLoad<List<uint>>("old", TimeSpan.FromDays(1), out _), "an older schema must not load");
Check(!File.Exists(Path.Combine(cacheDir, "cache-old.json")), "a rejected cache file is deleted");

// Corrupt json is discarded too.
File.WriteAllText(Path.Combine(cacheDir, "cache-broken.json"), "{ this is not json");
Check(!cache.TryLoad<List<uint>>("broken", TimeSpan.FromDays(1), out _), "corrupt cache must not load");
Check(!File.Exists(Path.Combine(cacheDir, "cache-broken.json")), "a corrupt cache file is deleted");

// Trimming keeps the newest files only.
for (var i = 0; i < 6; i++)
{
    cache.Save($"trim{i}", payload);
    System.Threading.Thread.Sleep(10);
}

var removedFiles = cache.Trim(keep: 3);
var remaining = Directory.GetFiles(cacheDir, "cache-*.json").Length;
Console.WriteLine($"  trim removed {removedFiles} file(s), {remaining} left");
Check(remaining <= 3, $"trim should keep at most 3 files, left {remaining}");

cache.Clear();
Check(!cache.TryLoad<List<uint>>("sample", TimeSpan.FromHours(1), out _), "clear should remove entries");
Directory.Delete(cacheDir, recursive: true);

// ------------------------------------------------------------------- urls ----
Section("request urls");

var aggregatedUrl = UniversalisClient.AggregatedUrl("https://universalis.app/", "Coeurl", new uint[] { 1, 2, 3 });
Check(aggregatedUrl == "https://universalis.app/api/v2/aggregated/Coeurl/1,2,3", $"unexpected aggregated url: {aggregatedUrl}");

var quotesUrl = UniversalisClient.QuotesUrl("https://universalis.app", "Coeurl", new uint[] { 5333 }, 60, 120);
Check(
    quotesUrl == "https://universalis.app/api/v2/Coeurl/5333?listings=60&entries=120",
    $"unexpected quotes url: {quotesUrl}");

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("ALL CHECKS PASSED");
    return 0;
}

Console.WriteLine($"{failures.Count} FAILURE(S):");
foreach (var failure in failures.Distinct())
    Console.WriteLine($"  - {failure}");
return 1;
