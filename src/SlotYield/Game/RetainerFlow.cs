using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SlotYield.Core;

namespace SlotYield.Game;

/// <summary>
/// Moves between retainers the way Dagobert does: click a row in RetainerList with the addon's own
/// callback, pick "Sell items" from the retainer menu, and close the windows when done.
/// </summary>
public static unsafe class RetainerFlow
{
    /// <summary>"Sell items" position in the retainer menu. Index 2 in English, matching Dagobert.</summary>
    public const int SellItemsEntry = 2;

    public static bool IsOpen(string name) =>
        Plugin.GameGui.GetAddonByName<AtkUnitBase>(name, 1) is not null;

    public static bool ClickRetainer(int index)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerList", 1);

        if (addon is null)
            return false;

        Fire(addon, true, 2, index);
        return true;
    }

    public static bool SelectSellItems(int entry = SellItemsEntry)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("SelectString", 1);

        if (addon is null)
            return false;

        Fire(addon, true, entry);
        return true;
    }

    public static bool Close(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name, 1);

        if (addon is null)
            return false;

        addon->Close(true);
        return true;
    }

    /// <summary>The retainer id of the retainer whose menu is open, or 0.</summary>
    public static ulong ActiveRetainerId()
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            var active = manager is null || !manager->IsReady ? null : manager->GetActiveRetainer();
            return active is null ? 0 : active->RetainerId;
        }
        catch
        {
            return 0;
        }
    }

    public static int SellListCount()
    {
        try
        {
            var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentRetainer.Instance();
            return agent is null ? 0 : agent->SellListEntryCount;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>A retainer with free market slots, preferring the one already open.</summary>
    public static ulong FirstWithSpace(int capacity = 20)
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return 0;

            var active = manager->GetActiveRetainer();
            if (active is not null && active->RetainerId != 0 && active->MarketItemCount < capacity)
                return active->RetainerId;

            foreach (var retainer in manager->Retainers)
            {
                if (retainer.RetainerId != 0 && retainer.MarketItemCount < capacity)
                    return retainer.RetainerId;
            }
        }
        catch
        {
            // not logged in
        }

        return 0;
    }

    public static int MarketCount(ulong retainerId)
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return 0;

            foreach (var retainer in manager->Retainers)
            {
                if (retainer.RetainerId == retainerId)
                    return retainer.MarketItemCount;
            }
        }
        catch
        {
            // not logged in
        }

        return 0;
    }

    public static List<ulong> RetainersWithListings()
    {
        var result = new List<ulong>();

        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return result;

            foreach (var retainer in manager->Retainers)
            {
                if (retainer.RetainerId != 0 && retainer.MarketItemCount > 0)
                    result.Add(retainer.RetainerId);
            }
        }
        catch
        {
            // not logged in
        }

        return result;
    }

    public static List<(uint ItemId, bool Hq)> SellListItems()
    {
        var result = new List<(uint, bool)>();

        try
        {
            var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentRetainer.Instance();
            if (agent is null)
                return result;

            var entries = agent->SellListEntries;
            var count = Math.Min(entries.Length, Math.Max(0, agent->SellListEntryCount));

            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry.ItemId == 0 || entry.Quantity <= 0)
                    continue;

                var itemId = ListingBook.NormalizeItemId(entry.ItemId, out var hq);
                result.Add((itemId, hq));
            }
        }
        catch
        {
            // list mid-load
        }

        return result;
    }

    public static bool ClickSellRow(int index)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSellList", 1);
        if (addon is null)
            return false;

        // Dagobert opens the row menu with these two values.
        Fire(addon, true, 0, index, 1);
        return true;
    }

    public static bool ChooseAdjustPrice()
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("ContextMenu", 1);
        if (addon is null)
            return false;

        Fire(addon, true, 0, 0, 0, 0, 0);
        return true;
    }

    public static bool ClickComparePrices()
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("RetainerSell", 1);
        if (addon is null)
            return false;

        Fire(addon, true, 4);
        return true;
    }

    /// <summary>Reads the price currently shown for a listing in the open sell list.</summary>
    public static bool TryReadListingPrice(uint itemId, out int price)
    {
        price = 0;

        try
        {
            var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentRetainer.Instance();
            if (agent is null)
                return false;

            var entries = agent->SellListEntries;
            var count = Math.Min(entries.Length, Math.Max(0, agent->SellListEntryCount));

            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];
                var id = ListingBook.NormalizeItemId(entry.ItemId, out _);

                if (id != itemId)
                    continue;

                var value = Money.ParseGil(entry.UnitPriceText.ToString());
                if (value <= 0)
                    continue;

                price = (int)value;
                return true;
            }
        }
        catch
        {
            // list mid-load
        }

        return false;
    }

    public static int IndexById(ulong retainerId)
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return -1;

            var retainers = manager->Retainers;

            for (var i = 0; i < retainers.Length; i++)
            {
                if (retainers[i].RetainerId == retainerId)
                    return i;
            }
        }
        catch
        {
            // not logged in
        }

        return -1;
    }

    private static void Fire(AtkUnitBase* addon, bool close, params int[] values)
    {
        var count = values.Length;
        var array = stackalloc AtkValue[count];

        for (var i = 0; i < count; i++)
        {
            array[i].Type = AtkValueType.Int;
            array[i].Int = values[i];
        }

        addon->FireCallback((uint)count, array, close);
    }
}
