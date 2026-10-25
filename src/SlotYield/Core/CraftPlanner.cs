using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

/// <summary>
/// One thing you could make right now from materials you already hold - directly, or through
/// intermediate crafts you can also make from those same materials.
/// </summary>
/// <param name="Profit">Revenue minus what the materials would fetch on the market.</param>
/// <param name="OwnedProfit">Revenue only - the materials are already yours.</param>
/// <param name="UsesIntermediates">True when at least one ingredient has to be crafted first.</param>
public sealed record CraftOpportunity(
    uint ItemId,
    int Crafts,
    int Produced,
    decimal Revenue,
    decimal MaterialValue,
    decimal Profit,
    decimal OwnedProfit,
    double DaysToSell,
    bool MaterialsPriced,
    int CraftJob,
    int RequiredLevel,
    bool UsesIntermediates,
    decimal HqRevenue,
    decimal HqProfit,
    bool HqAvailable);

/// <summary>
/// Answers "given what is in my bags, what is worth crafting?".
///
/// Feasibility is a recursive stock simulation: a recipe is only suggested when every ingredient is
/// either in the bag or can itself be made from what is in the bag, within a depth limit. Costs use
/// the same recursive calculator as the market side, so an intermediate is valued at what its raw
/// materials would fetch rather than at zero.
/// </summary>
public static class CraftPlanner
{
    public const int DefaultLimit = 60;

    private const int MaxSubCraftDepth = 4;

    public static List<CraftOpportunity> Plan(
        IEnumerable<CraftRecipe> recipes,
        IReadOnlyDictionary<uint, int> owned,
        Func<uint, Quality, MarketQuote?> quoteFor,
        Func<uint, decimal?> priceFor,
        SlotParameters parameters,
        int limit = DefaultLimit)
    {
        var recipeList = recipes as IList<CraftRecipe> ?? recipes.ToList();

        var byResult = new Dictionary<uint, CraftRecipe>(recipeList.Count);
        foreach (var recipe in recipeList)
            byResult[recipe.ItemId] = recipe;

        // Anything you hold counts; anything whose ingredients all count, counts too. This prunes the
        // tens of thousands of recipes that cannot possibly be made from the current stock.
        var reachable = Reachable(recipeList, owned);

        var calculator = new CostCalculator(
            new MapRecipeSource(byResult),
            new FuncPriceSource(priceFor),
            CostMode.Best);

        var found = new List<CraftOpportunity>();

        foreach (var recipe in recipeList)
        {
            if (!reachable.Contains(recipe.ItemId))
                continue;

            if (recipe.ResultAmount <= 0 || recipe.Ingredients.Count == 0)
                continue;

            var stock = new Dictionary<uint, int>(owned);
            var crafts = int.MaxValue;
            var usesIntermediates = false;
            var materialsPriced = true;
            var materialValuePerCraft = 0m;

            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Amount <= 0)
                {
                    crafts = 0;
                    break;
                }

                if (owned.GetValueOrDefault(ingredient.ItemId) < ingredient.Amount)
                    usesIntermediates = true;

                var units = MaxUnits(ingredient.ItemId, stock, byResult, 1);
                crafts = Math.Min(crafts, units / ingredient.Amount);

                if (crafts <= 0)
                    break;

                var cost = calculator.Compute(ingredient.ItemId, ingredient.Amount);

                if (cost is null || cost.Partial)
                {
                    materialsPriced = false;
                }
                else
                {
                    materialValuePerCraft += cost.PerUnit * ingredient.Amount;
                }
            }

            if (crafts <= 0 || crafts == int.MaxValue)
                continue;

            var quote = quoteFor(recipe.ItemId, Quality.Nq);
            if (quote is null || !quote.HasData || quote.MinListing <= 0)
                continue;

            var produced = crafts * recipe.ResultAmount;
            var net = quote.MinListing * (1m - parameters.TaxRate);
            var revenue = produced * net;
            var totalMaterialValue = materialValuePerCraft * crafts;

            var competitors = Math.Max(0, quote.SellersAtMinPrice);
            var share = Math.Min(SlotModel.MaxAssumedShare, 1.0 / (competitors + 1));
            var unitsPerDay = quote.DailyVelocity * share;
            var days = unitsPerDay > 0 ? produced / unitsPerDay : double.PositiveInfinity;

            // The HQ variant of the same craft, when the recipe can make one and the market has it.
            var hqRevenue = 0m;
            var hqProfit = 0m;
            var hqAvailable = false;

            if (recipe.CanHq && quoteFor(recipe.ItemId, Quality.Hq) is { HasData: true } hqQuote && hqQuote.MinListing > 0)
            {
                hqRevenue = produced * hqQuote.MinListing * (1m - parameters.TaxRate);
                hqProfit = hqRevenue - totalMaterialValue;
                hqAvailable = true;
            }

            found.Add(new CraftOpportunity(
                recipe.ItemId,
                crafts,
                produced,
                revenue,
                totalMaterialValue,
                revenue - totalMaterialValue,
                revenue,
                days,
                materialsPriced,
                recipe.CraftJob,
                recipe.CraftLevel,
                usesIntermediates,
                hqRevenue,
                hqProfit,
                hqAvailable));
        }

        return found
            .OrderByDescending(opportunity => Math.Max(opportunity.Profit, opportunity.HqProfit))
            .ThenByDescending(opportunity => opportunity.OwnedProfit)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Item ids whose recipe could plausibly be made from the stock, highest craft level first. Used
    /// to decide which outputs are worth pricing before the planner can rank them.
    /// </summary>
    public static IReadOnlyList<uint> ReachableResults(
        IEnumerable<CraftRecipe> recipes,
        IReadOnlyDictionary<uint, int> owned,
        int limit = 60)
    {
        var recipeList = recipes as IList<CraftRecipe> ?? recipes.ToList();
        var reachable = Reachable(recipeList, owned);

        var levels = new Dictionary<uint, int>();
        var results = new List<uint>();

        foreach (var recipe in recipeList)
        {
            if (recipe.ItemId == 0 || !reachable.Contains(recipe.ItemId))
                continue;

            if (!levels.TryGetValue(recipe.ItemId, out var level))
            {
                results.Add(recipe.ItemId);
                level = 0;
            }

            if (recipe.CraftLevel > level)
                levels[recipe.ItemId] = recipe.CraftLevel;
        }

        results.Sort((a, b) => levels.GetValueOrDefault(b).CompareTo(levels.GetValueOrDefault(a)));
        return results.Take(limit).ToList();
    }

    private static HashSet<uint> Reachable(
        IList<CraftRecipe> recipeList,
        IReadOnlyDictionary<uint, int> owned)
    {
        var reachable = new HashSet<uint>(owned.Keys);

        for (var pass = 0; pass < MaxSubCraftDepth; pass++)
        {
            var grew = false;

            foreach (var recipe in recipeList)
            {
                if (reachable.Contains(recipe.ItemId) || recipe.Ingredients.Count == 0)
                    continue;

                var allReachable = true;

                foreach (var ingredient in recipe.Ingredients)
                {
                    if (reachable.Contains(ingredient.ItemId))
                        continue;

                    allReachable = false;
                    break;
                }

                if (!allReachable)
                    continue;

                reachable.Add(recipe.ItemId);
                grew = true;
            }

            if (!grew)
                break;
        }

        return reachable;
    }

    /// <summary>
    /// How many units of an item the stock can yield: what is in the bag, plus what can be crafted
    /// from the rest of it. Consumption is shared with the caller's stock copy, so competing recipes
    /// do not each get the full pile.
    /// </summary>
    private static int MaxUnits(
        uint itemId,
        Dictionary<uint, int> stock,
        IReadOnlyDictionary<uint, CraftRecipe> byResult,
        int depth)
    {
        var have = stock.GetValueOrDefault(itemId);
        stock[itemId] = 0;

        if (depth >= MaxSubCraftDepth)
            return have;

        if (!byResult.TryGetValue(itemId, out var recipe) || recipe.ResultAmount <= 0)
            return have;

        var crafts = int.MaxValue;

        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient.Amount <= 0)
                return have;

            var units = MaxUnits(ingredient.ItemId, stock, byResult, depth + 1);
            crafts = Math.Min(crafts, units / ingredient.Amount);

            if (crafts == 0 && have == 0)
                return 0;
        }

        if (crafts == int.MaxValue || crafts <= 0)
            return have;

        return have + (crafts * recipe.ResultAmount);
    }

    private sealed class MapRecipeSource : IRecipeSource
    {
        private readonly IReadOnlyDictionary<uint, CraftRecipe> byResult;

        public MapRecipeSource(IReadOnlyDictionary<uint, CraftRecipe> byResult)
        {
            this.byResult = byResult;
        }

        public CraftRecipe? GetRecipe(uint itemId) =>
            this.byResult.TryGetValue(itemId, out var recipe) ? recipe : null;
    }

    private sealed class FuncPriceSource : IPriceSource
    {
        private readonly Func<uint, decimal?> price;

        public FuncPriceSource(Func<uint, decimal?> price)
        {
            this.price = price;
        }

        public decimal? UnitPrice(uint itemId) => this.price(itemId);
    }
}
