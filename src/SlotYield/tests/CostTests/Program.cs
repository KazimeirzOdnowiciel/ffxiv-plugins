using SlotYield.Core;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void Section(string name) => Console.WriteLine($"\n== {name}");

// A tiny synthetic economy: recipes by result item, prices by item.
var recipes = new Dictionary<uint, CraftRecipe>
{
    // A1 = 2 x B2 + 1 x C3
    [1] = new(1, 1, new[] { new CraftIngredient(2, 2), new CraftIngredient(3, 1) }, 10, true),

    // B2 = 2 x D4
    [2] = new(2, 1, new[] { new CraftIngredient(4, 2) }, 5, false),

    // E5 = 3 x F6, yields 3
    [5] = new(5, 3, new[] { new CraftIngredient(6, 3) }, 20, true),

    // G7 needs H8 (unpriced) and B2
    [7] = new(7, 1, new[] { new CraftIngredient(8, 1), new CraftIngredient(2, 1) }, 30, false),

    // Crafting 11 is expensive; buying it is cheaper.
    [11] = new(11, 1, new[] { new CraftIngredient(2, 2) }, 15, false),

    // Cycle: X9 -> Y10 -> X9
    [9] = new(9, 1, new[] { new CraftIngredient(10, 1) }, 40, false),
    [10] = new(10, 1, new[] { new CraftIngredient(9, 1) }, 40, false),

    // Deep chain: D101 -> ... -> D106 (leaf priced)
    [101] = new(101, 1, new[] { new CraftIngredient(102, 1) }, 50, false),
    [102] = new(102, 1, new[] { new CraftIngredient(103, 1) }, 50, false),
    [103] = new(103, 1, new[] { new CraftIngredient(104, 1) }, 50, false),
    [104] = new(104, 1, new[] { new CraftIngredient(105, 1) }, 50, false),
    [105] = new(105, 1, new[] { new CraftIngredient(106, 1) }, 50, false),
};

var prices = new Dictionary<uint, decimal>
{
    [1] = 100m,
    [2] = 100m,
    [3] = 50m,
    [4] = 10m,
    [6] = 30m,
    [11] = 30m,
    // 8 is deliberately unpriced: a gathered material with no market data.
};

var priceSource = new FakePrices(prices);
var recipeSource = new FakeRecipes(recipes);

CostCalculator Calc(CostMode mode) => new(recipeSource, priceSource, mode);

// ------------------------------------------------------------------ basics ----
Section("craft cost");

// A1 crafts from a crafted subcomponent: B2 = 2 x 10 = 20, so A1 = 2 x 20 + 50 = 90.
var craft = Calc(CostMode.Craft).Compute(1)!;
Console.WriteLine($"  A1 craft: {craft.PerUnit} gil/unit, partial={craft.Partial}, {craft.Detail}");
Check(craft.PerUnit == 90m, $"2 crafted B2 (20 each) + 50 = 90, got {craft.PerUnit}");
Check(!craft.Partial, "every material was priced");
Check(craft.Source == "craft", "source should be craft");

var market = Calc(CostMode.Market).Compute(1)!;
Console.WriteLine($"  A1 market: {market.PerUnit}");
Check(market.PerUnit == 100m, $"buying A1 costs 100, got {market.PerUnit}");
Check(market.Source == "market", "source should be market");

// Best takes the cheaper route: crafting at 90 beats buying at 100.
var best = Calc(CostMode.Best).Compute(1)!;
Console.WriteLine($"  A1 best: {best.PerUnit} ({best.Source})");
Check(best.PerUnit == 90m && best.Source == "craft", $"crafting at 90 wins, got {best.PerUnit} {best.Source}");

// And the other way round: crafting 11 costs 2 x 20 = 40, buying costs 30.
var bestBuy = Calc(CostMode.Best).Compute(11)!;
Console.WriteLine($"  11 best: {bestBuy.PerUnit} ({bestBuy.Source})");
Check(bestBuy.PerUnit == 30m && bestBuy.Source == "market", $"buying at 30 wins, got {bestBuy.PerUnit} {bestBuy.Source}");
Check(Calc(CostMode.Craft).Compute(11)!.PerUnit == 40m, "crafting 11 from two B2 is 40");

// Sub-craft on its own.
Check(Calc(CostMode.Craft).Compute(2)!.PerUnit == 20m, "B2 crafts for 2 x 10");
Check(Calc(CostMode.Best).Compute(2)!.Source == "craft", "B2 should be crafted at 20 rather than bought at 100");

// ------------------------------------------------------------------ yield ----
Section("yield and rounding");

var yieldCalc = Calc(CostMode.Craft);
var oneUnit = yieldCalc.Compute(5, 1)!;
Console.WriteLine($"  E5 x1: {oneUnit.PerUnit}");
Check(oneUnit.PerUnit == 30m, $"3 units from 3 x 30 = 30 each, got {oneUnit.PerUnit}");

var fourUnits = yieldCalc.Compute(5, 4)!;
Console.WriteLine($"  E5 x4: {fourUnits.PerUnit} gil/unit (two crafts of three)");
Check(fourUnits.PerUnit == 30m, $"2 crafts produce 6 units for 180, 30 each, got {fourUnits.PerUnit}");

// ---------------------------------------------------------------- partial ----
Section("partial costs");

var partial = Calc(CostMode.Craft).Compute(7)!;
Console.WriteLine($"  G7: {partial.PerUnit} gil/unit, partial={partial.Partial}, {partial.Detail}");
Check(partial.Partial, "an unpriced material makes the cost partial");
Check(partial.PerUnit == 20m, $"only the crafted B2 is priced (20), got {partial.PerUnit}");

// Cycles must not hang. The result can be null or partial; the point is that it returns.
var cycle = Calc(CostMode.Craft).Compute(9);
Console.WriteLine($"  cycle 9: {(cycle is null ? "null" : cycle.PerUnit.ToString())}");

// Depth limit: five levels of crafting cannot reach the priced leaf at depth 4.
var deep = Calc(CostMode.Craft).Compute(101);
Console.WriteLine($"  deep chain: {(deep is null ? "null" : $"{deep.PerUnit} partial={deep.Partial}")}");
Check(deep is null || deep.Partial, "a chain deeper than the limit must be partial or unknown");

// --------------------------------------------------------------- revenue ----
Section("revenue mode");

Check(Calc(CostMode.Revenue).Compute(1) is null, "revenue mode computes no cost");

// ---------------------------------------------------------- profit model ----
Section("profit model");

var quote = new MarketQuote
{
    ItemId = 1,
    Quality = Quality.Nq,
    MinListing = 1000m,
    AverageSale = 1200m,
    DailyVelocity = 10,
    ListingCount = 5,
    DistinctSellers = 3,
    SellersAtMinPrice = 3,
    HistorySamples = 20,
    HistorySpanDays = 10,
    FetchedUtc = DateTime.UtcNow,
};

var parameters = new SlotParameters { Strategy = PricingStrategy.MatchMinimum, TaxRate = 0.05m, StackSize = 99, DeadAfterDays = 45 };
var score = SlotModel.Score(quote, parameters);
var withCost = SlotModel.ApplyCost(score, new ItemCost(600m, false, "craft", "test"), expectCost: true);

Console.WriteLine($"  revenue {score.GilPerDay}/day, cost {withCost.CostPerUnit}, profit {withCost.ProfitPerUnit}/unit, {withCost.ProfitPerDay}/day");
Check(withCost.CostPerUnit == 600m, "cost should carry through");
Check(withCost.ProfitPerUnit == 350m, $"950 net - 600 cost = 350, got {withCost.ProfitPerUnit}");
Check(withCost.ProfitPerDay == 875m, $"2.5 units/day x 350 = 875, got {withCost.ProfitPerDay}");
Check(!withCost.CostIsPartial, "a complete cost is not partial");

var noCost = SlotModel.ApplyCost(score, null, expectCost: true);
Check(noCost.ProfitPerDay == score.GilPerDay, "without a cost, profit falls back to revenue");
Check(noCost.CostIsPartial, "a missing cost is flagged partial");

var loss = SlotModel.ApplyCost(score, new ItemCost(1200m, false, "craft", "test"), expectCost: true);
Check(loss.ProfitPerUnit == -250m, $"selling 950 net against 1200 cost is a 250 loss, got {loss.ProfitPerUnit}");
Check(loss.ProfitPerDay < 0, "a loss must stay negative, not be clamped");

var deadScore = SlotModel.ApplyCost(SlotModel.Score(quote with { DailyVelocity = 0.01 }, parameters), new ItemCost(10m, false, "craft", "t"), true);
Check(deadScore.ProfitPerDay == 0m, "dead items have no profit");

// ---------------------------------------------------------------- portfolio ----
Section("portfolio uses profit");

var candidates = new[]
{
    new PortfolioCandidate(1, Quality.Nq, 60_000m, 5_000m),
    new PortfolioCandidate(2, Quality.Nq, 10_000m, 1_000m),
};
var plan = PortfolioOptimizer.Optimize(candidates, slots: 1, budget: 100_000m);
Check(plan.Picks.Single().ItemId == 1, "the optimizer should take the higher profit per slot");

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

internal sealed class FakeRecipes : IRecipeSource
{
    private readonly IReadOnlyDictionary<uint, CraftRecipe> recipes;

    public FakeRecipes(IReadOnlyDictionary<uint, CraftRecipe> recipes) => this.recipes = recipes;

    public CraftRecipe? GetRecipe(uint itemId) => this.recipes.TryGetValue(itemId, out var recipe) ? recipe : null;
}

internal sealed class FakePrices : IPriceSource
{
    private readonly IReadOnlyDictionary<uint, decimal> prices;

    public FakePrices(IReadOnlyDictionary<uint, decimal> prices) => this.prices = prices;

    public decimal? UnitPrice(uint itemId) =>
        this.prices.TryGetValue(itemId, out var price) && price > 0 ? price : null;
}
