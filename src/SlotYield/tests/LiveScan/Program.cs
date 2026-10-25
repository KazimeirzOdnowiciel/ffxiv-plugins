using System.Diagnostics;
using System.Text.Json;
using SlotYield.Core;
using SlotYield.Market;

// Live end-to-end run of the scanner against Universalis.
//
//   dotnet run --project tests/LiveScan -- [world] [topN] [budgetMillions]
//
// Item names are looked up from Garland Tools for display only; the plugin itself reads names
// from the game's own sheets.

var world = args.Length > 0 ? args[0] : "Light";
var topN = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 15;
var budgetMillions = args.Length > 2 && decimal.TryParse(args[2], out var b) ? b : 20m;

var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");

using var client = new UniversalisClient(maxConcurrency: 4);
var cache = new MarketCache(cacheDir, message => Console.WriteLine($"  [cache] {message}"));

var options = new ScanOptions
{
    World = world,
    ShortlistSize = 400,
    BatchSize = 100,
    CacheMaxAge = TimeSpan.FromHours(6),
    Model = new SlotParameters
    {
        Strategy = PricingStrategy.MatchMinimum,
        TaxRate = 0.03m,
        StackSize = 99,
        DeadAfterDays = 45,
        MinimumVelocity = 0.2,
    },
};

var progress = new Progress<ScanProgress>(p =>
{
    if (p.Total <= 1)
        return;

    Console.Write($"\r  {p.Phase,-10} {p.Done,4}/{p.Total,-4} {p.Detail,-40}");
});

Console.WriteLine($"Scanning {world}...");
var stopwatch = Stopwatch.StartNew();

ScanResult result;
try
{
    result = await new MarketScanner(client, cache).ScanAsync(options, progress, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nscan failed: {ex.Message}");
    return 1;
}

stopwatch.Stop();
Console.WriteLine();
Console.WriteLine(
    $"  {result.Stats.AggregatedItems} items seen, {result.Stats.PricedItems} priced, " +
    $"{result.Stats.Requests} requests, {result.Stats.Duration.TotalSeconds:0.0}s, " +
    $"aggregates {(result.Stats.AggregatesFromCache ? "cached" : "fresh")}, quotes {(result.Stats.QuotesFromCache ? "cached" : "fresh")}");

foreach (var warning in result.Warnings)
    Console.WriteLine($"  ! {warning}");

var live = result.Candidates.Where(c => !c.Score.IsDead && !c.Score.IsSuspect).ToList();
var suspect = result.Candidates.Count(c => c.Score.IsSuspect);
Console.WriteLine($"  {live.Count} live candidates, {suspect} flagged as suspicious volume, {result.Candidates.Count - live.Count - suspect} dead");

// ------------------------------------------------------------------ names ----

var names = new Dictionary<uint, string>();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("SlotYield/0.1");

foreach (var id in live.Take(topN).Select(c => c.ItemId).Distinct())
{
    try
    {
        var json = await http.GetStringAsync($"https://garlandtools.org/db/doc/item/en/3/{id}.json");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("item", out var item) && item.TryGetProperty("name", out var name))
            names[id] = name.GetString() ?? $"#{id}";
    }
    catch
    {
        names[id] = $"#{id}";
    }
}

// ------------------------------------------------------------------ table ----

Console.WriteLine();
Console.WriteLine($"Top {topN} slots on {world} by gil/day (match-min, 3% tax, 99-unit stack)");
Console.WriteLine(new string('-', 116));
Console.WriteLine(
    $"{"#",-3} {"item",-38} {"q",-3} {"price",-9} {"sales/d",-8} {"units/d",-8} {"share",-6} {"gil/day",-10} {"capital",-9} {"trend",-7} {"clear",-6}");
Console.WriteLine(new string('-', 116));

var rank = 1;
foreach (var candidate in live.Take(topN))
{
    var score = candidate.Score;
    var name = names.GetValueOrDefault(candidate.ItemId, $"#{candidate.ItemId}");
    if (name.Length > 36)
        name = name[..35] + "~";

    var trendText = score.TrendPercent.ToString("+0%;-0%;0%");

    Console.WriteLine(
        $"{rank++,-3} {name,-38} {(candidate.Quality == Quality.Hq ? "HQ" : "NQ"),-3} " +
        $"{Money.Gil(score.ListPrice),-9} {score.Quote.DailyVelocity,-5:0.0} {score.UnitsPerDay,-8:0.0} " +
        $"{score.ExpectedShare,-6:P0} {Money.Gil(score.GilPerDay),-10} {Money.Gil(score.CapitalPerSlot),-9} " +
        $"{trendText,-7} {Money.Days(score.DaysToClear),-6}");
}

// -------------------------------------------------------------- portfolio ----

var candidates = live
    .GroupBy(c => (c.ItemId, c.Quality))
    .Select(g => g.First())
    .Select(c => new PortfolioCandidate(c.ItemId, c.Quality, c.Score.CapitalPerSlot, c.Score.GilPerDay))
    .ToList();

var plan = PortfolioOptimizer.Optimize(candidates, slots: 20, budget: budgetMillions * 1_000_000m);

Console.WriteLine();
Console.WriteLine(
    $"Best 20 slots within {budgetMillions:0}M gil: {Money.Gil(plan.GilPerDay)}/day, " +
    $"{Money.Gil(plan.Capital)} tied up, weakest slot worth {Money.Gil(plan.ShadowSlotValue)}/day, " +
    $"+1M gil would add {Money.Gil(plan.MarginalGilPerMillion)}/day");

foreach (var pick in plan.Picks.Take(10))
{
    var name = names.GetValueOrDefault(pick.ItemId, $"#{pick.ItemId}");
    Console.WriteLine($"  - {name} ({(pick.Quality == Quality.Hq ? "HQ" : "NQ")})  {Money.Gil(pick.GilPerDay)}/day  {Money.Gil(pick.CapitalPerSlot)} capital");
}

Console.WriteLine();
Console.WriteLine("done");
return 0;
