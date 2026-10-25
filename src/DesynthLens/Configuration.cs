using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace DesynthLens;

/// <summary>What one item has actually produced when desynthesised, and what that was worth.</summary>
public sealed record DesynthRecord(
    uint ItemId,
    string Name,
    int Times,
    decimal TotalValue,
    decimal BestValue,
    decimal LastValue,
    string LastYield,
    DateTime LastUtc,
    decimal BuyPrice = 0m,
    decimal TotalProfit = 0m,
    bool Untradeable = false);

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool WindowOpen { get; set; } = true;

    /// <summary>Desynth results observed in game, keyed by the item that was desynthesised.</summary>
    public List<DesynthRecord> Records { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void Normalize()
    {
        this.Records ??= new List<DesynthRecord>();

        if (this.Records.Count > 400)
            this.Records.RemoveRange(0, this.Records.Count - 400);
    }
}
