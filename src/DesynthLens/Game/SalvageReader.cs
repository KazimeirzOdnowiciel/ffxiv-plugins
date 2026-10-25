using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DesynthLens.Game;

/// <summary>Reads the desynthesis result the game is currently showing.</summary>
public static unsafe class SalvageReader
{
    /// <summary>The item that was desynthesised, and what came out of it with quantities.</summary>
    public static bool TryRead(out uint itemId, out List<(uint ItemId, int Quantity)> results)
    {
        itemId = 0;
        results = new List<(uint, int)>();

        try
        {
            var agent = AgentSalvage.Instance();
            if (agent is null)
                return false;

            itemId = agent->DesynthItemId;
            if (itemId == 0)
                return false;

            var span = agent->DesynthResults;

            foreach (var result in span)
            {
                if (result.ItemId == 0 || result.Quantity <= 0)
                    continue;

                results.Add((result.ItemId, result.Quantity));
            }

            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsResultOpen()
    {
        try
        {
            var agent = AgentSalvage.Instance();
            return agent is not null && agent->IsSalvageResultAddonOpen;
        }
        catch
        {
            return false;
        }
    }
}
