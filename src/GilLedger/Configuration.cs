using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace GilLedger;

public enum GilKind
{
    /// <summary>Sold something on the market board through a retainer.</summary>
    Sale,

    /// <summary>Bought something from the market board or a vendor.</summary>
    Purchase,

    /// <summary>Market board listing or sale tax.</summary>
    ListingTax,

    /// <summary>Mender repair.</summary>
    Repair,

    /// <summary>Retainer venture fee.</summary>
    Venture,

    /// <summary>Sold something to a vendor.</summary>
    VendorSell,

    /// <summary>Gil moved to or from a retainer.</summary>
    Retainer,

    /// <summary>The balance moved and nothing in chat explained it.</summary>
    Unattributed,
}

/// <summary>One change to the player's pocket gil.</summary>
[Serializable]
public sealed class GilEntry
{
    public long Id { get; set; }

    public DateTime Utc { get; set; }

    public GilKind Kind { get; set; }

    /// <summary>Signed: positive is income, negative is expense.</summary>
    public long Amount { get; set; }

    /// <summary>Pocket balance right after this entry.</summary>
    public long BalanceAfter { get; set; }

    public string Note { get; set; } = string.Empty;
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>Reconcile every balance change the chat parser cannot explain.</summary>
    public bool ReconcileBalance { get; set; } = true;

    /// <summary>Try to label the balance spike that follows a retainer sale.</summary>
    public bool WatchRetainerSales { get; set; } = true;

    public bool WindowOpen { get; set; }

    public List<GilEntry> Entries { get; set; } = new();

    public long NextId { get; set; } = 1;

    public void Normalize()
    {
        this.Entries ??= new List<GilEntry>();
        if (this.NextId < 1)
            this.NextId = 1;

        if (this.Entries.Count > 20_000)
            this.Entries.RemoveRange(0, this.Entries.Count - 20_000);
    }
}
