using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using SlotYield.Core;

namespace SlotYield.Game;

/// <summary>
/// Recipe lookup built once from the game's sheets.
///
/// An item can have several recipes (different levels, different yields). The cheapest one to level
/// into is the useful default, so the lowest recipe level wins; ties keep the higher yield.
/// </summary>
public sealed class RecipeCatalog : IRecipeSource
{
    private readonly Dictionary<uint, CraftRecipe> byResult;

    private RecipeCatalog(Dictionary<uint, CraftRecipe> byResult)
    {
        this.byResult = byResult;
    }

    public int Count => this.byResult.Count;

    public IEnumerable<CraftRecipe> All => this.byResult.Values;

    public CraftRecipe? GetRecipe(uint itemId) =>
        this.byResult.TryGetValue(itemId, out var recipe) ? recipe : null;

    public static RecipeCatalog Build(IDataManager data, Action<string>? log = null)
    {
        var map = new Dictionary<uint, CraftRecipe>();

        try
        {
            var sheet = data.GetExcelSheet<Recipe>();
            if (sheet is null)
            {
                log?.Invoke("recipe sheet unavailable");
                return new RecipeCatalog(map);
            }

            foreach (var row in sheet)
            {
                var result = row.ItemResult.RowId;
                var amount = (int)row.AmountResult;
                if (result == 0 || amount <= 0)
                    continue;

                var ingredients = new List<CraftIngredient>(8);
                var items = row.Ingredient;
                var amounts = row.AmountIngredient;

                for (var i = 0; i < items.Count; i++)
                {
                    var itemId = items[i].RowId;
                    var quantity = i < amounts.Count ? amounts[i] : 0;
                    if (itemId != 0 && quantity > 0)
                        ingredients.Add(new CraftIngredient(itemId, quantity));
                }

                if (ingredients.Count == 0)
                    continue;

                var level = row.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0;
                // CraftType 0-7 are CRP..CUL, which are ClassJob ids 8-15.
                var craftType = (int)row.CraftType.RowId;
                var job = craftType is >= 0 and <= 7 ? craftType + 8 : 0;

                var recipe = new CraftRecipe(result, amount, ingredients, level, row.CanHq, job);

                if (!map.TryGetValue(result, out var existing)
                    || level < existing.CraftLevel
                    || (level == existing.CraftLevel && amount > existing.ResultAmount))
                {
                    map[result] = recipe;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"recipe build failed: {ex.Message}");
        }

        log?.Invoke($"recipes: {map.Count}");
        return new RecipeCatalog(map);
    }
}
