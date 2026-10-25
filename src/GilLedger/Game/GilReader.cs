using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;
using GilLedger.Core;

namespace GilLedger.Game;

/// <summary>
/// Reads the player's current pocket gil through ClientStructs, the same way Slot Yield does.
/// </summary>
public static unsafe class GilReader
{
    public static long Gil()
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            var container = manager is null
                ? null
                : manager->GetInventoryContainer(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Currency);

            if (container is null || !container->IsLoaded)
                return 0;

            var slot = container->GetInventorySlot(0);
            return slot is null ? 0 : slot->GetQuantity();
        }
        catch
        {
            return 0;
        }
    }
}
