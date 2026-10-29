using SlotYield.Core;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void Section(string name) => Console.WriteLine($"\n== {name}");

MarketQuote Quote(
    double velocity = 10,
    decimal min = 1000m,
    decimal avg = 1200m,
    int sellersAtMin = 3,
    int distinctSellers = 5,
    SlotYield.Core.Quality quality = SlotYield.Core.Quality.Nq) => new()
{
    ItemId = 1,
    Quality = quality,
    MinListing = min,
    AverageSale = avg,
    DailyVelocity = velocity,
    ListingCount = 12,
    DistinctSellers = distinctSellers,
    SellersAtMinPrice = sellersAtMin,
    HistorySamples = 40,
    HistorySpanDays = 7,
    FetchedUtc = DateTime.UtcNow,
    LastUploadUtc = DateTime.UtcNow.AddMinutes(-5),
};

// ------------------------------------------------------------------ model ----
Section("slot model");

var parameters = new SlotParameters
{
    Strategy = PricingStrategy.MatchMinimum,
    TaxRate = 0.05m,
    StackSize = 99,
    DeadAfterDays = 45,
    MinimumVelocity = 0.2,
};

var score = SlotModel.Score(Quote(), parameters);
Console.WriteLine($"match-min: price={score.ListPrice} share={score.ExpectedShare:P0} units/day={score.UnitsPerDay:0.00} net={score.NetPerUnit} gil/day={score.GilPerDay} days={score.DaysToClear:0.0}");

Check(score.ListPrice == 1000m, "list price should be the min listing");
Check(Math.Abs(score.ExpectedShare - 0.25) < 1e-9, $"share should be 1/4, got {score.ExpectedShare}");
Check(Math.Abs(score.UnitsPerDay - 2.5) < 1e-9, $"units/day should be 2.5, got {score.UnitsPerDay}");
Check(score.NetPerUnit == 950m, $"net should be 950, got {score.NetPerUnit}");
Check(score.GilPerDay == 2375m, $"gil/day should be 2375, got {score.GilPerDay}");
Check(score.CapitalPerSlot == 99_000m, $"capital should be 99k, got {score.CapitalPerSlot}");
Check(Math.Abs(score.DaysToClear - 39.6) < 1e-9, $"days should be 39.6, got {score.DaysToClear}");
Check(!score.IsDead, "a 39.6 day clear under a 45 day limit is not dead");

var undercut = SlotModel.Score(Quote(), parameters with { Strategy = PricingStrategy.UndercutMinimum });
Console.WriteLine($"undercut: price={undercut.ListPrice} share={undercut.ExpectedShare:P0} gil/day={undercut.GilPerDay}");
Check(undercut.ListPrice == 999m, "undercut price should be 999");
Check(Math.Abs(undercut.ExpectedShare - 0.5) < 1e-9, "undercut share should hit the 50% cap");
Check(undercut.NetPerUnit == 949m, $"999 at 5% tax should net 949, got {undercut.NetPerUnit}");

var premium = SlotModel.Score(Quote(avg: 5000m), parameters with { Strategy = PricingStrategy.MatchAverageSale });
Console.WriteLine($"premium: price={premium.ListPrice} share={premium.ExpectedShare:P0} gil/day={premium.GilPerDay}");
Check(premium.ListPrice == 5000m, "premium price should follow the average sale");
Check(Math.Abs(premium.ExpectedShare - 1.0 / 6.0) < 1e-9, "premium share should be 1/(all sellers + 1)");
Check(premium.IsDead, "listing at the average sale takes 59 days for a stack and must read as dead");

// Dead cases.
var unsold = SlotModel.Score(Quote(velocity: 0), parameters);
Check(unsold.IsDead && unsold.GilPerDay == 0m, "zero velocity must be dead and worth nothing");

var trickle = SlotModel.Score(Quote(velocity: 0.1), parameters);
Check(trickle.IsDead, "velocity below the floor must be dead");

var slow = SlotModel.Score(Quote(velocity: 1, sellersAtMin: 0, distinctSellers: 1), parameters);
Check(slow.IsDead, $"1 sale/day with a 99 stack takes 99 days and must be dead ({slow.DaysToClear:0.0})");

var noPrice = SlotModel.Score(Quote(min: 0m, velocity: 5), parameters);
Check(noPrice.IsDead, "no listings means no data and no slot");

var fast = SlotModel.Score(Quote(velocity: 60, min: 500m), parameters);
Check(fast.Verdict == "fast mover", $"60 sales/day should read fast, got '{fast.Verdict}'");
Check(fast.UnitsPerDay <= 99, "units per day can never exceed the stack");

// Tax rounding: 999 * 0.97 = 969.03 -> 969.
var threePercent = SlotModel.Score(Quote(), parameters with { TaxRate = 0.03m });
Check(threePercent.NetPerUnit == 970m, $"1000 at 3% tax should net 970, got {threePercent.NetPerUnit}");

var odd = SlotModel.Score(Quote(min: 333m), parameters with { TaxRate = 0.03m });
Check(odd.NetPerUnit == 323m, $"333 at 3% tax should net 323 (323.01 rounds down), got {odd.NetPerUnit}");

// Share can never be above the cap, however empty the market.
var emptyMarket = SlotModel.Score(Quote(sellersAtMin: 0, distinctSellers: 0), parameters);
Check(emptyMarket.ExpectedShare <= SlotModel.MaxAssumedShare, "share must respect the cap");

// The explanation must at least mention the pieces it used.
Check(score.Explain.Contains("2.50") && score.Explain.Contains("2375") || score.Explain.Contains("2,375"), "explain text should carry the numbers");

// ------------------------------------------------------------- portfolio ----
Section("portfolio optimizer");

PortfolioCandidate Candidate(uint id, decimal capital, decimal perDay) =>
    new(id, SlotYield.Core.Quality.Nq, capital, perDay);

var simple = new[]
{
    Candidate(1, 1_000_000m, 10_000m),
    Candidate(2, 500_000m, 8_000m),
    Candidate(3, 2_000_000m, 30_000m),
    Candidate(4, 100_000m, 1_000m),
};

var plan = PortfolioOptimizer.Optimize(simple, slots: 2, budget: 3_000_000m);
Console.WriteLine($"plan: {plan.Picks.Count} picks, {plan.GilPerDay:N0} gil/day, {plan.Capital:N0} capital, shadow slot {plan.ShadowSlotValue:N0}");
Check(plan.Picks.Count == 2, "two slots should pick two items");
Check(plan.GilPerDay == 40_000m, $"best pair is 30k + 10k = 40k (exactly 3M capital), got {plan.GilPerDay}");
Check(plan.Capital <= 3_000_000m, "plan must respect the budget");
Check(plan.Picks.Select(p => p.ItemId).Distinct().Count() == plan.Picks.Count, "an item cannot occupy two slots");

var budgetBound = PortfolioOptimizer.Optimize(simple, slots: 4, budget: 700_000m);
Console.WriteLine($"budget-bound: {string.Join(",", budgetBound.Picks.Select(p => p.ItemId))} -> {budgetBound.GilPerDay:N0}");
Check(budgetBound.Capital <= 700_000m, "budget-bound plan must respect the budget");
Check(budgetBound.Picks.All(p => p.ItemId is 2 or 4), "only the cheap items fit in 700k");

var empty = PortfolioOptimizer.Optimize(simple, slots: 0, budget: 1_000_000m);
Check(empty.Picks.Count == 0 && empty.GilPerDay == 0m, "zero slots means an empty plan");

var noAffordable = PortfolioOptimizer.Optimize(new[] { Candidate(9, 5_000_000m, 50_000m) }, slots: 4, budget: 1_000_000m);
Check(noAffordable.Picks.Count == 0, "nothing affordable means an empty plan");

// Monotonicity: more budget or more slots can never make the plan worse.
var tight = PortfolioOptimizer.Optimize(simple, 2, 1_000_000m);
var looseBudget = PortfolioOptimizer.Optimize(simple, 2, 5_000_000m);
var looseSlots = PortfolioOptimizer.Optimize(simple, 4, 5_000_000m);
Check(looseBudget.GilPerDay >= tight.GilPerDay, "more budget cannot lose money");
Check(looseSlots.GilPerDay >= looseBudget.GilPerDay, "more slots cannot lose money");

// ------------------------------------------- brute-force cross validation ----
Section("brute force cross-check");

var rng = new Random(20261022);
var mismatches = 0;

for (var trial = 0; trial < 400; trial++)
{
    var count = rng.Next(1, 11);
    var slots = rng.Next(1, 5);
    // Budget and capitals are multiples of 5,000 so the bucket discretisation is exact.
    var budget = rng.Next(1, 21) * 5_000m;

    var items = new List<PortfolioCandidate>();
    for (var i = 0; i < count; i++)
        items.Add(Candidate((uint)(i + 1), rng.Next(1, 21) * 5_000m, rng.Next(0, 100) * 100m));

    var dpPlan = PortfolioOptimizer.Optimize(items, slots, budget);

    // Exhaustive best over all subsets that fit.
    decimal bestValue = 0m;
    for (var mask = 0; mask < (1 << count); mask++)
    {
        var used = 0;
        var capital = 0m;
        var value = 0m;

        for (var i = 0; i < count; i++)
        {
            if ((mask & (1 << i)) == 0)
                continue;

            used++;
            capital += items[i].CapitalPerSlot;
            value += items[i].GilPerDay;
        }

        if (used > slots || capital > budget)
            continue;

        if (value > bestValue)
            bestValue = value;
    }

    if (dpPlan.GilPerDay != bestValue)
    {
        mismatches++;
        if (mismatches <= 5)
            Console.WriteLine($"  MISMATCH trial {trial}: dp={dpPlan.GilPerDay} brute={bestValue} slots={slots} budget={budget}");
    }

    if (dpPlan.Capital > budget)
        failures.Add($"trial {trial}: plan over budget ({dpPlan.Capital} > {budget})");

    if (dpPlan.Picks.Count > slots)
        failures.Add($"trial {trial}: plan uses too many slots");
}

Check(mismatches == 0, $"{mismatches} of 400 random instances disagree with brute force");
Console.WriteLine("  400 random instances checked against exhaustive search");

// ---------------------------------------------------------------- scan diff ----
Section("scan diff");

SnapshotEntry Entry(uint id, decimal price, decimal profit = 1000m, Quality quality = Quality.Nq) =>
    new(id, quality, price, profit, 10);

var before = new ScanSnapshot(DateTime.UtcNow.AddHours(-6), "Light", new[]
{
    Entry(1, 1000m),
    Entry(2, 500m),
    Entry(3, 900m),
    Entry(4, 250m),
});

var after = new ScanSnapshot(DateTime.UtcNow, "Light", new[]
{
    Entry(1, 800m),
    Entry(2, 500m),
    Entry(3, 1200m),
    Entry(5, 300m),
});

var diff = SnapshotDiff.Compare(before, after);
Console.WriteLine($"  drops={diff.Drops.Count} rises={diff.Rises.Count} new={diff.NewEntries.Count} gone={diff.Gone.Count}");
Check(diff.PreviousCount == 4 && diff.CurrentCount == 4, "counts should reflect both snapshots");
Check(diff.Drops.Count == 1 && diff.Drops[0].ItemId == 1, "item 1 dropped 20%");
Check(Math.Abs(diff.Drops[0].PriceChangePercent + 0.20) < 1e-9, $"drop should be -20%, got {diff.Drops[0].PriceChangePercent:P1}");
Check(diff.Rises.Count == 1 && diff.Rises[0].ItemId == 3, "item 3 rose 33%");
Check(diff.NewEntries.Count == 1 && diff.NewEntries[0].ItemId == 5, "item 5 is new");
Check(diff.Gone.Count == 1 && diff.Gone[0].ItemId == 4, "item 4 dropped out");
Check(diff.HasAnything, "a changed scan has something to say");

var noChange = SnapshotDiff.Compare(before, before);
Check(!noChange.HasAnything, "an unchanged scan says nothing");

// Two percent is noise.
var noise = SnapshotDiff.Compare(before, new ScanSnapshot(DateTime.UtcNow, "Light", new[]
{
    Entry(1, 1020m),
    Entry(2, 500m),
    Entry(3, 900m),
    Entry(4, 250m),
}));
Check(!noise.HasAnything, "a 2% wobble is not a move");

// First scan ever: everything is new, nothing crashes.
var first = SnapshotDiff.Compare(ScanSnapshot.Empty, after);
Check(first.NewEntries.Count > 0 && first.Drops.Count == 0, "the first scan has only arrivals");

// A zero previous price must not divide by zero.
var zeroBase = SnapshotDiff.Compare(
    new ScanSnapshot(DateTime.UtcNow.AddHours(-1), "Light", new[] { Entry(9, 0m) }),
    new ScanSnapshot(DateTime.UtcNow, "Light", new[] { Entry(9, 100m) }));
Check(zeroBase.Drops.Count == 0 && zeroBase.Rises.Count == 0, "a zero base price produces no percentage");

// Snapshots have to survive the cache round trip.
var snapshotJson = System.Text.Json.JsonSerializer.Serialize(after);
var snapshotBack = System.Text.Json.JsonSerializer.Deserialize<ScanSnapshot>(snapshotJson);
Check(
    snapshotBack is not null && snapshotBack.Entries.Count == after.Entries.Count && snapshotBack.Entries[0].ItemId == 1,
    "a snapshot must survive json round-trip");

// Snapshot selection: top N by profit, plus pinned items even when they are outside it.
var ranked = Enumerable.Range(1, 6)
    .Select(i => new SnapshotEntry((uint)i, Quality.Nq, 1000m, 1000m - (i * 100m), 10))
    .ToList();

var pinned = new HashSet<(uint, Quality)> { (6, Quality.Nq) };
var selected = SnapshotBuilder.Select(ranked, pinned, top: 3);
Console.WriteLine($"  snapshot selected {selected.Count}: {string.Join(",", selected.Select(e => e.ItemId))}");
Check(selected.Count == 4, "top three plus the pinned item");
Check(selected.Any(e => e.ItemId == 6), "a pinned item outside the top is still tracked");

var pinnedInsideTop = new HashSet<(uint, Quality)> { (1, Quality.Nq) };
Check(SnapshotBuilder.Select(ranked, pinnedInsideTop, top: 3).Count == 3, "a pinned item in the top must not duplicate");

Check(
    SnapshotBuilder.Select(ranked, new HashSet<(uint, Quality)> { (99, Quality.Nq) }, top: 3).Count == 3,
    "pinned items that are not in the scan are ignored");

// Confidence falls with age and sample count.
var freshQuote = new MarketQuote
{
    ItemId = 1,
    Quality = Quality.Nq,
    MinListing = 1000m,
    AverageSale = 1000m,
    DailyVelocity = 5,
    DistinctSellers = 6,
    SellersAtMinPrice = 2,
    HistorySamples = 20,
    FetchedUtc = DateTime.UtcNow,
    LastUploadUtc = DateTime.UtcNow,
};

var freshConfidence = SlotModel.Confidence(freshQuote);
var oldConfidence = SlotModel.Confidence(freshQuote with { LastUploadUtc = DateTime.UtcNow.AddHours(-12) });
var noSamples = SlotModel.Confidence(freshQuote with { HistorySamples = 0 });
Console.WriteLine($"  confidence fresh={freshConfidence:0.00} 12h={oldConfidence:0.00} no-samples={noSamples:0.00}");
Check(freshConfidence > 0.7, $"fresh, sampled data should be confident, got {freshConfidence:0.00}");
Check(oldConfidence < freshConfidence * 0.8, "a twelve-hour-old board is less trustworthy");
Check(noSamples == 0, "no sale samples means no confidence");

var staleScore = SlotModel.Score(freshQuote with { LastUploadUtc = DateTime.UtcNow.AddHours(-10) }, parameters);
Check(staleScore.IsStale, "a ten-hour-old quote is stale");
Check(!SlotModel.Score(freshQuote, parameters).IsStale, "a fresh quote is not stale");

// -------------------------------------------------- optimizer performance ----
Section("optimizer performance");

var perfCandidates = Enumerable.Range(1, 400)
    .Select(i => new PortfolioCandidate(
        (uint)i,
        Quality.Nq,
        ((i % 50) * 200_000m) + 100_000m,
        (((i % 97) * 137m) + 100m)))
    .ToList();

var perfTimer = System.Diagnostics.Stopwatch.StartNew();
var bigPlan = PortfolioOptimizer.Optimize(perfCandidates, slots: 20, budget: 20_000_000m);
perfTimer.Stop();

Console.WriteLine(
    $"  400 candidates, 20 slots, 20M budget: {bigPlan.Picks.Count} picks, {perfTimer.ElapsedMilliseconds} ms, truncated={bigPlan.Truncated}");

Check(perfTimer.ElapsedMilliseconds < 3000, $"optimizer took {perfTimer.ElapsedMilliseconds} ms");
Check(bigPlan.Capital <= 20_000_000m, "performance plan must respect the budget");

// ---------------------------------------------------------------- csv export ----
Section("csv");

Check(CsvExport.Escape("plain") == "plain", "plain values are untouched");
Check(CsvExport.Escape("a,b") == "\"a,b\"", "commas force quotes");
Check(CsvExport.Escape("say \"hi\"") == "\"say \"\"hi\"\"\"", "quotes are doubled");
Check(CsvExport.Escape("line\nbreak") == "\"line\nbreak\"", "newlines force quotes");

var csv = CsvExport.Build(
    new[] { "item", "price" },
    new List<IReadOnlyList<string>>
    {
        new[] { "Wind Shard", "12" },
        new[] { "Odd, Name", "34" },
    });
Console.WriteLine("  " + csv.Replace("\n", " / ").TrimEnd('/').TrimEnd());
Check(csv.StartsWith("item,price\n"), "header row first");
Check(csv.Contains("\"Odd, Name\",34"), "quoted field survives");
Check(csv.EndsWith("\n"), "rows end with a newline");

// ------------------------------------------------------------- listing book ----
Section("my listings");

CapturedListing Listing(uint itemId, ulong retainer, uint price, uint qty = 10, bool hq = false, int ageMinutes = 0) =>
    new(itemId, hq, price, qty, (itemId * 100) + retainer, retainer, $"Retainer {retainer}", 3,
        DateTime.UtcNow.AddMinutes(-ageMinutes));

Check(ListingBook.NormalizeItemId(36208, out var plainId) == 36208 && !plainId, "a base item id passes through");
Check(ListingBook.NormalizeItemId(1036208, out var hqId) == 36208 && hqId, "the HQ offset is stripped");

var encoded = new List<CapturedListing>
{
    new(1036208, true, 1498, 1, 0, 7, "R", 0, DateTime.UtcNow),
};
Check(ListingBook.RepairEncodedIds(encoded) == 1, "one encoded listing repaired");
Check(encoded.Count == 1 && encoded[0].ItemId == 36208 && encoded[0].Hq, "id fixed and quality kept");

var duplicates = new List<CapturedListing>
{
    new(1036208, true, 1498, 1, 0, 7, "R", 0, DateTime.UtcNow.AddMinutes(-5)),
    new(36208, true, 1200, 1, 0, 7, "R", 0, DateTime.UtcNow),
};
Check(ListingBook.RepairEncodedIds(duplicates) == 1, "the encoded duplicate is merged");
Check(duplicates.Count == 1 && duplicates[0].PricePerUnit == 1200, "the newest capture wins the merge");

var book = new List<CapturedListing>();

Check(ListingBook.Upsert(book, Listing(100, 1, 1000)), "a new listing is a change");
Check(book.Count == 1, "one listing in the book");

Check(!ListingBook.Upsert(book, Listing(100, 1, 1000)), "the same slot with the same numbers is not a change");
Check(book.Count == 1, "re-seeing a listing does not duplicate it");

Check(ListingBook.Upsert(book, Listing(100, 1, 1200)), "repricing is a change");
Check(book.Count == 1, "repricing updates the slot instead of adding a row");
Check(book[0].PricePerUnit == 1200, "the price is updated");

Check(ListingBook.Upsert(book, Listing(100, 2, 1200)), "the same item in another retainer is another slot");
Check(ListingBook.Upsert(book, Listing(100, 1, 1200, hq: true)), "the same item in HQ is another slot");
Check(book.Count == 3, $"expected three slots, got {book.Count}");

// A full book keeps the most recently seen entries.
var fullBook = new List<CapturedListing>();
for (var i = 0; i < ListingBook.MaxEntries + 20; i++)
    ListingBook.Upsert(fullBook, Listing((uint)(i + 1), 1, 1000, ageMinutes: i));

Check(fullBook.Count == ListingBook.MaxEntries, $"the book is capped at {ListingBook.MaxEntries}, got {fullBook.Count}");
Check(fullBook.All(l => l.ItemId <= ListingBook.MaxEntries), "the oldest entries are the ones dropped");

var pruned = new List<CapturedListing>
{
    Listing(1, 1, 100, ageMinutes: 60 * 24 * 40),
    Listing(2, 1, 100, ageMinutes: 10),
};
Check(ListingBook.ForgetOlderThan(pruned, TimeSpan.FromDays(30), DateTime.UtcNow) == 1, "one stale listing forgotten");
Check(pruned.Count == 1 && pruned[0].ItemId == 2, "the fresh listing survives");

// Reviews.
var reviewQuote = new MarketQuote
{
    ItemId = 100,
    Quality = Quality.Nq,
    MinListing = 1000m,
    AverageSale = 1100m,
    DailyVelocity = 10,
    ListingCount = 6,
    DistinctSellers = 3,
    SellersAtMinPrice = 1,
    HistorySamples = 20,
    FetchedUtc = DateTime.UtcNow,
};

var atMarket = ListingBook.Review(Listing(100, 1, 1000), reviewQuote, parameters);
var above = ListingBook.Review(Listing(100, 1, 1200), reviewQuote, parameters);
var cheapest = ListingBook.Review(Listing(100, 1, 900), reviewQuote, parameters);
var unpriced = ListingBook.Review(Listing(100, 1, 900), null, parameters);

Console.WriteLine($"  at market: {atMarket.Status} {atMarket.DaysToSell:0.0}d");
Console.WriteLine($"  above: {above.Status} {above.PriceDeltaPercent:P0} {above.DaysToSell:0.0}d");
Check(atMarket.Status == ListingStatus.AtMarket, "listing at the minimum is at market");
Check(Math.Abs(atMarket.PriceDeltaPercent) < 1e-9, "no delta at the minimum");
Check(Math.Abs(atMarket.DaysToSell - 2.0) < 1e-9, $"10 units at half of 10/day takes 2 days, got {atMarket.DaysToSell}");

Check(above.Status == ListingStatus.AboveMarket, "listing above the minimum is behind the queue");
Check(Math.Abs(above.PriceDeltaPercent - 0.2) < 1e-9, $"1200 vs 1000 is +20%, got {above.PriceDeltaPercent:P0}");
Check(above.DaysToSell > atMarket.DaysToSell, "being above market must be slower");

Check(cheapest.Status == ListingStatus.Cheapest, "listing under the minimum is cheapest");
Check(unpriced.Status == ListingStatus.Unpriced && unpriced.MarketMin == 0m, "no quote means unpriced");

Check(above.SuggestedPrice == 1000m, $"suggested price matches the minimum, got {above.SuggestedPrice}");
Check(above.DaysIfRepriced < above.DaysToSell, "repricing must clear faster than sitting above the market");
Check(above.Advice.StartsWith("reprice ", StringComparison.Ordinal), $"advice tells you to reprice, got: {above.Advice}");
Check(atMarket.Advice.Contains("minimum", StringComparison.Ordinal), $"at-market advice, got: {atMarket.Advice}");

var oldListing = new CapturedListing(100, false, 1000, 1, 0, 1, "R", 0, DateTime.UtcNow.AddDays(-10));
var soldReview = ListingBook.Review(oldListing, reviewQuote, parameters);
Check(soldReview.IsProbablySold, "a listing unseen for ten days is probably sold");
Check(soldReview.Advice.Contains("not seen", StringComparison.Ordinal), $"stale advice, got: {soldReview.Advice}");
Check(!ListingBook.Review(Listing(100, 1, 1000), reviewQuote, parameters).IsProbablySold, "a fresh listing is not flagged");

// ------------------------------------------------------------ craft planner ----
Section("craft planner");

var craftRecipes = new[]
{
    new CraftRecipe(1000, 1, new[] { new CraftIngredient(10, 2), new CraftIngredient(11, 1) }, 50, true),
    new CraftRecipe(1001, 3, new[] { new CraftIngredient(10, 4) }, 50, false),
    new CraftRecipe(1002, 1, new[] { new CraftIngredient(12, 1) }, 50, false),
    new CraftRecipe(1003, 1, new[] { new CraftIngredient(13, 1) }, 50, false),
};

var ownedStock = new Dictionary<uint, int> { [10] = 10, [11] = 2, [13] = 1 };

MarketQuote CraftQuote(uint id, decimal price, double velocity) => new()
{
    ItemId = id, Quality = Quality.Nq, MinListing = price, DailyVelocity = velocity,
    SellersAtMinPrice = 1, HistorySamples = 10, FetchedUtc = DateTime.UtcNow,
};

var craftQuotes = new Dictionary<uint, MarketQuote>
{
    [1000] = CraftQuote(1000, 1000m, 10),
    [1001] = CraftQuote(1001, 500m, 4),
    [1003] = CraftQuote(1003, 2000m, 2),
};

MarketQuote? CraftQuoteFor(uint id, Quality quality) => craftQuotes.GetValueOrDefault(id);
decimal? CraftPriceFor(uint id) => id switch { 10 => 100m, 11 => 50m, _ => null };

var craftPlan = CraftPlanner.Plan(craftRecipes, ownedStock, CraftQuoteFor, CraftPriceFor, parameters);
Console.WriteLine($"  {craftPlan.Count} idea(s): " + string.Join(", ", craftPlan.Select(o => $"#{o.ItemId} x{o.Crafts} = {o.Profit}")));

Check(craftPlan.Count == 3, $"three recipes match the bags, got {craftPlan.Count}");
Check(craftPlan.All(o => o.ItemId != 1002), "a recipe missing a material is not suggested");

var rice = craftPlan.Single(o => o.ItemId == 1001);
Check(rice.Crafts == 2 && rice.Produced == 6, $"10 of a 4-per-craft material makes 2 crafts / 6 units, got {rice.Crafts}/{rice.Produced}");
Check(rice.Revenue == 2850m, $"6 x 500 net of tax = 2850, got {rice.Revenue}");
Check(rice.MaterialValue == 800m, $"2 crafts x 4 x 100 = 800 of materials, got {rice.MaterialValue}");
Check(rice.Profit == 2050m, $"profit after material value should be 2050, got {rice.Profit}");
Check(rice.OwnedProfit == rice.Revenue, "owned profit is the revenue");

var doublet = craftPlan.Single(o => o.ItemId == 1000);
Check(doublet.Crafts == 2, "the scarce ingredient caps the crafts");
Check(doublet.Profit == 1400m, $"2 x net 1000 - 2 x 250 = 1400, got {doublet.Profit}");

var craftNoPriceWin = craftPlan.Single(o => o.ItemId == 1003);
Check(!craftNoPriceWin.MaterialsPriced, "a recipe with an unpriced material is flagged");
Check(craftPlan[0].Profit >= craftPlan[^1].Profit, "ideas are sorted by profit");


// HQ output: the same craft is worth more when the recipe can make HQ and the market pays for it.
var hqRecipes = new[]
{
    new CraftRecipe(50, 1, new[] { new CraftIngredient(10, 1) }, 50, true, 8),
};

var hqOwned = new Dictionary<uint, int> { [10] = 4 };

var hqQuotes = new Dictionary<uint, (MarketQuote Nq, MarketQuote Hq)>
{
    [50] = (CraftQuote(50, 1000m, 2), CraftQuote(50, 3000m, 2) with { Quality = Quality.Hq }),
};

MarketQuote? HqQuoteFor(uint id, Quality quality) =>
    hqQuotes.TryGetValue(id, out var pair) ? (quality == Quality.Hq ? pair.Hq : pair.Nq) : null;

decimal? HqPriceFor(uint id) => id == 10 ? 100m : null;

var hqPlan = CraftPlanner.Plan(hqRecipes, hqOwned, HqQuoteFor, HqPriceFor, parameters);
var hqIdea = hqPlan.Single();
Console.WriteLine($"  hq: nq={hqIdea.Profit} hq={hqIdea.HqProfit} available={hqIdea.HqAvailable}");

Check(hqIdea.HqAvailable, "a CanHq recipe with an HQ quote exposes the HQ variant");
Check(hqIdea.HqProfit > hqIdea.Profit, $"HQ sells for more than NQ, got {hqIdea.HqProfit} vs {hqIdea.Profit}");
Check(hqIdea.HqProfit == 11000m, $"4 x 3000 x 0.95 - 4 x 100 = 11000, got {hqIdea.HqProfit}");
Check(!craftPlan.Single(o => o.ItemId == 1001).HqAvailable, "a recipe that cannot make HQ has no HQ variant");

// Crafted intermediates: the sword is only makeable because the ingot is makeable first.
var chainRecipes = new[]
{
    new CraftRecipe(21, 1, new[] { new CraftIngredient(20, 2) }, 10, true, 8),
    new CraftRecipe(30, 1, new[] { new CraftIngredient(21, 1), new CraftIngredient(22, 1) }, 20, true, 8),
    new CraftRecipe(31, 1, new[] { new CraftIngredient(21, 1) }, 20, true, 8),
    new CraftRecipe(40, 1, new[] { new CraftIngredient(99, 1) }, 20, true, 8),
};

var chainOwned = new Dictionary<uint, int> { [20] = 10, [22] = 2 };

var chainQuotes = new Dictionary<uint, MarketQuote>
{
    [21] = CraftQuote(21, 500m, 5),
    [30] = CraftQuote(30, 5000m, 2),
    [31] = CraftQuote(31, 3000m, 2),
    [40] = CraftQuote(40, 9000m, 2),
};

MarketQuote? ChainQuoteFor(uint id, Quality quality) => chainQuotes.GetValueOrDefault(id);
decimal? ChainPriceFor(uint id) => id switch { 20 => 100m, 22 => 1000m, 21 => 500m, _ => null };

var reachableIds = CraftPlanner.ReachableResults(chainRecipes, chainOwned);
Check(reachableIds.Count == 3, $"three outputs are reachable, got {reachableIds.Count}");
Check(reachableIds.Contains(21u) && reachableIds.Contains(30u) && reachableIds.Contains(31u), "ingot and both swords are reachable");
Check(!reachableIds.Contains(40u), "the recipe with a missing raw material is not reachable");
Check(reachableIds[0] is 30 or 31, "higher-level crafts are priced first");

var chainPlan = CraftPlanner.Plan(chainRecipes, chainOwned, ChainQuoteFor, ChainPriceFor, parameters);
Console.WriteLine($"  chain: " + string.Join(", ", chainPlan.Select(o => $"#{o.ItemId} x{o.Crafts} sub={o.UsesIntermediates} mats={o.MaterialValue}")));

Check(chainPlan.Count == 3, $"three chain recipes are makeable, got {chainPlan.Count}");
Check(chainPlan.All(o => o.ItemId != 40), "a recipe whose raw material is absent is not suggested");

var sword = chainPlan.Single(o => o.ItemId == 30);
Check(sword.Crafts == 2, $"two gems cap the sword at 2 crafts, got {sword.Crafts}");
Check(sword.UsesIntermediates, "the sword needs a crafted ingot");
Check(sword.MaterialValue == 2400m, $"ingot at 200 + gem at 1000 per craft x2 = 2400, got {sword.MaterialValue}");

var chainSword = chainPlan.Single(o => o.ItemId == 31);
Check(chainSword.Crafts == 5, $"ten ore make five ingots, so five swords, got {chainSword.Crafts}");
Check(chainSword.MaterialValue == 1000m, $"five ingots at 2 x 100 = 1000, got {chainSword.MaterialValue}");
Check(chainSword.MaterialsPriced, "the ingot chain is fully priced");

var ingot = chainPlan.Single(o => o.ItemId == 21);
Check(!ingot.UsesIntermediates, "the ingot is made straight from the bag");
Check(ingot.Crafts == 5, $"ten ore make five ingots, got {ingot.Crafts}");

// --------------------------------------------------------------- repricing ----
Section("repricing");

var repriceQuote = new MarketQuote
{
    ItemId = 100, Quality = Quality.Nq, MinListing = 800m, DailyVelocity = 0.5,
    DistinctSellers = 2, SellersAtMinPrice = 1, HistorySamples = 10, FetchedUtc = DateTime.UtcNow,
};

MarketQuote? RepriceQuoteFor(uint id, Quality quality) =>
    id == 100 && quality == Quality.Nq ? repriceQuote : null;

var repriceListings = new List<CapturedListing>
{
    new(100, false, 1000, 1, 0, 7, "R", 0, DateTime.UtcNow),
};

var repricePlan = RepricePlanner.Build(repriceListings, RepriceQuoteFor, parameters, 2);
Check(repricePlan.Changes.Count == 1, $"one listing should move, got {repricePlan.Changes.Count}");
var move = repricePlan.Changes.Single();
Check(move.TargetPrice == 800m, $"target is the minimum, got {move.TargetPrice}");
Check(Math.Abs(move.DeltaPercent - 0.2) < 1e-9, $"1000 -> 800 is a 20% drop, got {move.DeltaPercent:P0}");
Check(move.DaysAfter < move.DaysNow, "the reprice should clear faster");
Console.WriteLine($"  reprice {move.CurrentPrice} -> {move.TargetPrice}: {move.DaysNow:0.0}d -> {move.DaysAfter:0.0}d");

var tinyListings = repriceListings.Select(l => l with { PricePerUnit = 805 }).ToList();

var tinyMove = RepricePlanner.Build(tinyListings, RepriceQuoteFor, parameters, minMoveGil: 10);
Check(tinyMove.IsEmpty && tinyMove.SkippedForSmallMove == 1, "a move under the gil threshold is skipped");

var tinyAllowed = RepricePlanner.Build(tinyListings, RepriceQuoteFor, parameters);
Check(tinyAllowed.Changes.Count == 1, "with no gil threshold, a 5 gil gap is still worth matching");

var crashQuote = repriceQuote with { MinListing = 300m };
MarketQuote? CrashQuoteFor(uint id, Quality quality) => id == 100 && quality == Quality.Nq ? crashQuote : null;

var guarded = RepricePlanner.Build(repriceListings, CrashQuoteFor, parameters, 2);
Check(guarded.IsEmpty && guarded.SkippedForFloor == 1, "a 70% drop is blocked by default");

var allowed = RepricePlanner.Build(repriceListings, CrashQuoteFor, parameters, 2, floorPercent: 0);
Check(allowed.Changes.Count == 1, "an explicit opt-in allows the big drop");

// --------------------------------------------------------------- net worth ----
Section("net worth");

var worthItems = new List<(uint ItemId, int Quantity)>
{
    (1, 10),   // priced 100  -> 1,000 (Materials)
    (2, 3),    // priced 500  -> 1,500 (Materials)
    (3, 1),    // priced 2,000 -> 2,000 (Gear)
    (4, 5),    // no price
};

decimal? WorthPrice(uint id) => id switch { 1 => 100m, 2 => 500m, 3 => 2_000m, _ => null };
string WorthCategory(uint id) => id == 3 ? "Gear" : "Materials";

var worth = NetWorth.Compute(worthItems, gil: 1_000m, WorthPrice, WorthCategory, DateTime.UtcNow);
Console.WriteLine($"  total={worth.Total} gil={worth.Gil} unpriced={worth.UnpricedItems} slices={string.Join("/", worth.Slices.Select(slice => $"{slice.Category}:{slice.Value}"))}");

Check(worth.Total == 5_500m, $"1k + 1.5k + 2k + 1k gil = 5.5k, got {worth.Total}");
Check(worth.UnpricedItems == 1, "one stack has no price and is counted, not valued");
Check(worth.Stacks == 4, "every stack is counted");
Check(worth.Slices.Count == 2, "categories are grouped");
Check(worth.Slices[0].Category == "Materials" && worth.Slices[0].Value == 2_500m, "the biggest category comes first");
Check(worth.Slices[1].Category == "Gear" && worth.Slices[1].Value == 2_000m, "the smaller category follows");
Check(worth.Items.Count == 3, $"three stacks were priced, got {worth.Items.Count}");
Check(worth.Items[0].Value == 2_000m, "items are sorted by value, biggest first");
Check(worth.Mode == WorthValuation.MinListing, "the valuation mode is carried on the snapshot");

var noHistory = NetWorth.ChangeSince(Array.Empty<WorthPoint>(), 1_000m);
Check(Math.Abs(noHistory) < 1e-9, "no history means no change");

var onePoint = new List<WorthPoint> { new(DateTime.UtcNow.AddDays(-1), 1_000m) };
Check(Math.Abs(NetWorth.ChangeSince(onePoint, 1_100m) - 0.10) < 1e-9, "1,000 -> 1,100 is +10%");

// ------------------------------------------------------------------ alerts ----
Section("alerts");

var alertNow = DateTime.UtcNow;
var alertQuote = new MarketQuote
{
    ItemId = 100,
    Quality = Quality.Nq,
    MinListing = 1000m,
    AverageSale = 1000m,
    DailyVelocity = 5,
    ListingCount = 6,
    DistinctSellers = 3,
    SellersAtMinPrice = 1,
    HistorySamples = 10,
    FetchedUtc = alertNow,
};

var myListing = new CapturedListing(100, false, 1200, 1, 0, 7, "R", 0, alertNow);

var undercutAlert = AlertRules.Undercut(myListing, alertQuote, 2, alertNow);
Check(undercutAlert is not null && undercutAlert.Kind == AlertKind.Undercut, "1200 above a 1000 minimum is undercut");
Check(undercutAlert!.Message.Contains("undercut", StringComparison.Ordinal), $"message names the problem: {undercutAlert.Message}");
Console.WriteLine($"  {undercutAlert.Message}");

Check(AlertRules.Undercut(myListing with { PricePerUnit = 1000 }, alertQuote, 2, alertNow) is null, "matching the minimum is not undercut");
Check(AlertRules.Undercut(myListing with { PricePerUnit = 1010 }, alertQuote, 2, alertNow) is null, "a 1% gap is inside the 2% threshold");
Check(AlertRules.Undercut(myListing, null, 2, alertNow) is null, "no quote means no alert");
Check(AlertRules.Undercut(myListing, alertQuote with { MinListing = 0m }, 2, alertNow) is null, "a zero minimum is ignored");

var expensive = alertQuote with { MinListing = 2000m };
var crashed = alertQuote with { MinListing = 1500m };
Check(AlertRules.PriceDrop(100, Quality.Nq, expensive, crashed, 15, alertNow) is not null, "a 25% fall crosses a 15% threshold");
Check(AlertRules.PriceDrop(100, Quality.Nq, expensive, alertQuote with { MinListing = 1900m }, 15, alertNow) is null, "a 5% fall does not");
Check(AlertRules.PriceDrop(100, Quality.Nq, expensive, null, 15, alertNow) is null, "no current quote means no alert");
Check(AlertRules.PriceDrop(100, Quality.Nq, expensive with { MinListing = 0m }, crashed, 15, alertNow) is null, "no baseline means no alert");

Check(AlertRules.CooldownElapsed(null, alertNow, TimeSpan.FromHours(1)), "never raised means the cooldown is over");
Check(!AlertRules.CooldownElapsed(alertNow.AddMinutes(-30), alertNow, TimeSpan.FromHours(1)), "half an hour into an hour is still cooling");
Check(AlertRules.CooldownElapsed(alertNow.AddHours(-2), alertNow, TimeSpan.FromHours(1)), "two hours later it can fire again");

var floorAlert = AlertRules.ProfitFloor(100, Quality.Nq, 80_000m, 20_000m, 50_000, alertNow);
Check(floorAlert is not null && floorAlert.Kind == AlertKind.ProfitFloor, "crossing the floor raises a profit alert");
Check(floorAlert!.Message.Contains("now worth", StringComparison.Ordinal), $"profit message: {floorAlert.Message}");
Check(AlertRules.ProfitFloor(100, Quality.Nq, 80_000m, 60_000m, 50_000, alertNow) is null, "staying above the floor is old news");
Check(AlertRules.ProfitFloor(100, Quality.Nq, 40_000m, 10_000m, 50_000, alertNow) is null, "below the floor is silent");
Check(AlertRules.ProfitFloor(100, Quality.Nq, 80_000m, null, 50_000, alertNow) is not null, "a newly pinned item can announce itself");

// ---------------------------------------------------------------- money ----
Section("formatting");

Check(Money.Gil(1_234_567m) == "1.23M", $"1,234,567 -> {Money.Gil(1_234_567m)}");
Check(Money.Gil(999m) == "999", $"999 -> {Money.Gil(999m)}");
Check(Money.Gil(45_600m) == "45.6k", $"45,600 -> {Money.Gil(45_600m)}");
Check(Money.Gil(0m) == "0", $"0 -> {Money.Gil(0m)}");
Check(Money.Days(double.PositiveInfinity) == "-", "infinite days renders as a dash");
Check(Money.Days(0.5) == "<1d", "a half day renders as <1d");
Check(Money.ParseGil("1,234") == 1234, $"thousands separator stripped, got {Money.ParseGil("1,234")}");
Check(Money.ParseGil("12 345") == 12345, "space separator stripped");
Check(Money.ParseGil("1.234") == 1234, "dot separator stripped");
Check(Money.ParseGil("") == 0 && Money.ParseGil(null) == 0, "empty text is zero");
Check(Money.ParseGil("no digits here") == 0, "text with no digits is zero");
Check(Money.ParseGil("9,999,999,999") == 999_999_999, "absurd prices are capped");

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
