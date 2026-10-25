using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace SlotYield.Game;

/// <summary>Identifies the item currently in the retainer sell dialog.</summary>
public static unsafe class SellDialog
{
    /// <summary>
    /// The item being listed or repriced, taken from the retainer agent's selected slot. Quality
    /// comes from the inventory item because the dialog itself does not expose it.
    /// </summary>
    public static bool TryGetItem(out uint itemId, out bool hq)
    {
        itemId = 0;
        hq = false;

        try
        {
            var agent = AgentRetainer.Instance();
            if (agent is null || agent->SellItemInventorySlot < 0)
                return false;

            var inventory = InventoryManager.Instance();
            if (inventory is null)
                return false;

            var container = inventory->GetInventoryContainer(agent->SellItemInventoryType);
            if (container is null || !container->IsLoaded)
                return false;

            var item = container->GetInventorySlot(agent->SellItemInventorySlot);
            if (item is null)
                return false;

            itemId = item->GetBaseItemId();
            hq = ((uint)item->Flags & 1u) != 0;
            return itemId != 0;
        }
        catch
        {
            return false;
        }
    }
}
