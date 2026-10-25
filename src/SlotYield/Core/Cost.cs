using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotYield.Core;

public enum CostMode
{
    /// <summary>Ignore costs; rank by revenue.</summary>
    Revenue,

    /// <summary>Assume you buy the finished item at market.</summary>
    Market,

    /// <summary>Assume you craft it; fall back to market when there is no recipe.</summary>
    Craft,

    /// <summary>Whichever is cheaper: craft it, or buy it.</summary>
    Best,
}

public sealed record CraftIngredient(uint ItemId, int Amount);

public sealed record CraftRecipe(
    uint ItemId,
    int ResultAmount,
    IReadOnlyList<CraftIngredient> Ingredients,
    int CraftLevel,
    bool CanHq,
    int CraftJob = 0);

public interface IRecipeSource
{
    CraftRecipe? GetRecipe(uint itemId);
}

/// <summary>Per-unit acquisition price for an item, or null when nothing is known.</summary>
public interface IPriceSource
{
    decimal? UnitPrice(uint itemId);
}

public sealed record ItemCost(decimal PerUnit, bool Partial, string Source, string Detail)
{
    public decimal ForQuantity(int quantity) => this.PerUnit * Math.Max(1, quantity);
}

/// <summary>
/// Works out what one unit of an item costs you: buy it, craft it, or the cheaper of the two.
///
/// Crafting recurses into sub-crafts with a depth limit and a cycle guard, because recipe data is
/// player-visible content and a malformed loop must not hang the UI. Materials with no known price
/// make the result <see cref="ItemCost.Partial"/> instead of silently zero, since a free material is
/// usually just a material we could not price.
/// </summary>
public sealed class CostCalculator
{
    public const int MaxDepth = 4;

    private const int MemoLimit = 50_000;

    private readonly IRecipeSource recipes;
    private readonly IPriceSource prices;
    private readonly CostMode mode;
    private readonly Dictionary<(uint ItemId, int Quantity), ItemCost?> memo = new();

    public CostCalculator(IRecipeSource recipes, IPriceSource prices, CostMode mode)
    {
        this.recipes = recipes;
        this.prices = prices;
        this.mode = mode;
    }

    public ItemCost? Compute(uint itemId, int quantity = 1) =>
        this.ComputeCore(itemId, Math.Max(1, quantity), 0, new HashSet<uint>());

    private ItemCost? ComputeCore(uint itemId, int quantity, int depth, HashSet<uint> visiting)
    {
        if (this.mode == CostMode.Revenue || depth > MaxDepth)
            return null;

        var key = (itemId, quantity);
        if (this.memo.TryGetValue(key, out var memoized))
            return memoized;

        // Materials are usually bought even when the finished item is crafted, so always ask for a price.
        var market = this.prices.UnitPrice(itemId);

        ItemCost? craft = null;
        if (this.mode is CostMode.Craft or CostMode.Best && !visiting.Contains(itemId))
        {
            var recipe = this.recipes.GetRecipe(itemId);
            if (recipe is not null)
            {
                visiting.Add(itemId);
                craft = this.CraftCost(recipe, quantity, depth, visiting);
                visiting.Remove(itemId);
            }
        }

        var result = this.Choose(itemId, market, craft);

        if (this.memo.Count < MemoLimit)
            this.memo[key] = result;

        return result;
    }

    private ItemCost? Choose(uint itemId, decimal? market, ItemCost? craft)
    {
        if (this.mode == CostMode.Market)
            return market is null ? null : new ItemCost(market.Value, false, "market", $"bought at {Money.Gil(market.Value)}");

        if (this.mode == CostMode.Craft)
        {
            if (craft is not null)
                return craft;

            return market is null
                ? null
                : new ItemCost(market.Value, false, "market", $"no recipe; bought at {Money.Gil(market.Value)}");
        }

        // Best: pick the cheaper way to obtain it.
        if (market is null && craft is null)
            return null;

        if (market is null)
            return craft;

        if (craft is null)
            return new ItemCost(market.Value, false, "market", $"bought at {Money.Gil(market.Value)}");

        return craft.PerUnit <= market.Value
            ? craft
            : new ItemCost(market.Value, false, "market", $"bought at {Money.Gil(market.Value)} (cheaper than crafting)");
    }

    private ItemCost? CraftCost(CraftRecipe recipe, int quantity, int depth, HashSet<uint> visiting)
    {
        var resultAmount = Math.Max(1, recipe.ResultAmount);
        var crafts = (int)Math.Ceiling(quantity / (double)resultAmount);

        var total = 0m;
        var partial = false;
        var priced = 0;

        foreach (var ingredient in recipe.Ingredients)
        {
            var needed = ingredient.Amount * crafts;
            var cost = this.ComputeCore(ingredient.ItemId, needed, depth + 1, visiting);

            if (cost is null)
            {
                partial = true;
                continue;
            }

            total += cost.ForQuantity(needed);
            partial |= cost.Partial;
            priced++;
        }

        if (priced == 0)
            return null;

        // Cost per unit produced: a craft that yields three makes three, even if only one is needed.
        var produced = crafts * resultAmount;
        var perUnit = total / produced;
        var detail = $"{crafts} craft{(crafts == 1 ? string.Empty : "s")} -> {produced} unit{(produced == 1 ? string.Empty : "s")}, {priced}/{recipe.Ingredients.Count} materials priced";
        if (partial)
            detail += " (some materials unpriced)";

        return new ItemCost(perUnit, partial, "craft", detail);
    }
}
