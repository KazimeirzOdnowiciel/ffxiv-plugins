using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace SlotYield.Game;

public sealed record ItemInfo(uint Id, string Name, string Category, int ItemLevel, bool CanBeHq, uint VendorPrice);

/// <summary>
/// Item names and categories from the game's own sheets. Lazily resolved and cached;
/// only ever called from the framework thread (the UI).
/// </summary>
public sealed class ItemCatalog
{
    private readonly IDataManager data;
    private readonly Dictionary<uint, ItemInfo> cache = new();

    public ItemCatalog(IDataManager data)
    {
        this.data = data;
    }

    public ItemInfo Get(uint id)
    {
        if (this.cache.TryGetValue(id, out var cached))
            return cached;

        var info = this.Resolve(id);
        this.cache[id] = info;
        return info;
    }

    public string Name(uint id) => this.Get(id).Name;

    /// <summary>ClassJob abbreviation for a crafting job id ("GSM"), or empty when unknown.</summary>
    public string JobAbbreviation(int jobId)
    {
        try
        {
            var sheet = this.data.GetExcelSheet<ClassJob>();
            if (sheet is null || jobId <= 0)
                return string.Empty;

            var row = sheet.GetRow((uint)jobId);
            return row.Abbreviation.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Vendor purchase price, or null when the item is not sold by a vendor.</summary>
    public decimal? VendorPrice(uint id)
    {
        var price = this.Get(id).VendorPrice;
        return price > 0 ? price : null;
    }

    /// <summary>
    /// Vendor purchase prices for every item, built in one pass on the framework thread so the scan
    /// can price materials off-thread without touching the game's sheets.
    /// </summary>
    public static IReadOnlyDictionary<uint, decimal> BuildVendorPriceMap(IDataManager data)
    {
        var map = new Dictionary<uint, decimal>(8192);

        try
        {
            var sheet = data.GetExcelSheet<Item>();
            if (sheet is null)
                return map;

            foreach (var row in sheet)
            {
                if (row.PriceMid > 0)
                    map[row.RowId] = row.PriceMid;
            }
        }
        catch
        {
            // A missing sheet just means no vendor fallback.
        }

        return map;
    }

    private ItemInfo Resolve(uint id)
    {
        try
        {
            var sheet = this.data.GetExcelSheet<Item>();
            if (sheet is null)
                return new ItemInfo(id, $"Item #{id}", string.Empty, 0, true, 0);

            var row = sheet.GetRow(id);
            if (row.RowId != id)
                return new ItemInfo(id, $"Item #{id}", string.Empty, 0, true, 0);

            var category = row.ItemUICategory.ValueNullable?.Name.ToString() ?? string.Empty;
            var level = (int)row.LevelItem.RowId;

            return new ItemInfo(id, row.Name.ToString(), category, level, row.CanBeHq, row.PriceMid);
        }
        catch
        {
            return new ItemInfo(id, $"Item #{id}", string.Empty, 0, true, 0);
        }
    }
}

public static class WorldDirectory
{
    /// <summary>Public worlds, sorted, for the world picker.</summary>
    public static IReadOnlyList<string> PublicWorlds(IDataManager data)
    {
        try
        {
            var sheet = data.GetExcelSheet<World>();
            if (sheet is null)
                return Array.Empty<string>();

            return sheet
                .Where(w => w is { IsPublic: true, RowId: > 0 })
                .Select(w => w.Name.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string CurrentWorld(IPlayerState player)
    {
        try
        {
            var world = player.CurrentWorld.ValueNullable;
            return world?.Name.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
