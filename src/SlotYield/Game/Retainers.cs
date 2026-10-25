using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SlotYield.Game;

public readonly record struct RetainerInfo(ulong Id, string Name, int MarketItemCount);

/// <summary>
/// Read-only view of the player's retainers. ClientStructs provides the layout, so there are no
/// hand-computed offsets here; the whole thing is wrapped because a half-loaded manager must never
/// take the plugin down.
/// </summary>
public static unsafe class Retainers
{
    public static List<RetainerInfo> All()
    {
        var result = new List<RetainerInfo>(10);

        try
        {
            var manager = RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return result;

            foreach (var retainer in manager->Retainers)
            {
                if (retainer.RetainerId == 0)
                    continue;

                result.Add(new RetainerInfo(
                    retainer.RetainerId,
                    retainer.NameString,
                    retainer.MarketItemCount));
            }
        }
        catch
        {
            // Not logged in, mid-zone, or the manager is being rebuilt.
        }

        return result;
    }

    public static bool IsMine(ulong retainerId, IReadOnlyList<RetainerInfo> mine)
    {
        foreach (var retainer in mine)
        {
            if (retainer.Id == retainerId)
                return true;
        }

        return false;
    }
}
