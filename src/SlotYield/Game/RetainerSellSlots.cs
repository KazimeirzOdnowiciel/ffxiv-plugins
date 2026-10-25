using System;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using SlotYield.Core;

namespace SlotYield.Game;

/// <summary>Maps an item to the retainer market slot currently showing it.</summary>
public static unsafe class RetainerSellSlots
{
    public static int FindSlot(uint itemId, bool hq)
    {
        try
        {
            var agent = AgentRetainer.Instance();
            if (agent is null)
                return -1;

            var entries = agent->SellListEntries;
            var live = Math.Min(entries.Length, Math.Max(0, agent->SellListEntryCount));

            var fallback = -1;

            for (var i = 0; i < live; i++)
            {
                var entry = entries[i];
                var id = ListingBook.NormalizeItemId(entry.ItemId, out var encoded);

                if (id != itemId)
                    continue;

                if (encoded == hq)
                    return entry.InventorySlot;

                if (fallback < 0)
                    fallback = entry.InventorySlot;
            }

            return fallback;
        }
        catch
        {
            // Not in a retainer.
        }

        return -1;
    }
}
