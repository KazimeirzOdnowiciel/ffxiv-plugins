using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using SlotYield.Core;

namespace SlotYield;

[Serializable]
public sealed class PinnedSlot
{
    public uint ItemId { get; set; }

    public bool Hq { get; set; }

    /// <summary>How many units you keep in the slot. 99 is a full stack.</summary>
    public int StackSize { get; set; } = 99;
}

[Serializable]
public sealed class ScanPulse
{
    public DateTime Utc { get; set; }

    public long GilPerDay { get; set; }
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>Universalis world name. Empty means "use my current world".</summary>
    public string WorldOverride { get; set; } = string.Empty;

    public PricingStrategy Strategy { get; set; } = PricingStrategy.MatchMinimum;

    /// <summary>Tax withheld by the market board. 3% outside the three city-states, 5% inside.</summary>
    public float TaxPercent { get; set; } = 3f;

    public int DefaultStackSize { get; set; } = 99;

    /// <summary>How item cost is estimated: revenue only, market, craft, or the cheaper of the two.</summary>
    public CostMode CostMode { get; set; } = CostMode.Best;

    public int PortfolioSlots { get; set; } = 20;

    public long PortfolioBudget { get; set; } = 20_000_000;

    /// <summary>Anything slower than this to clear a stack is flagged as a dead slot.</summary>
    public int DeadAfterDays { get; set; } = 45;

    /// <summary>Below this many units sold per day, an item is not worth a slot.</summary>
    public float MinimumVelocity { get; set; } = 0.2f;

    /// <summary>How many stage-2 candidates to price exactly after the cheap scan.</summary>
    public int ShortlistSize { get; set; } = 400;

    /// <summary>Hours a full market scan stays warm.</summary>
    public int CacheHours { get; set; } = 6;

    public int BatchSize { get; set; } = 100;

    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Minimum gil/day for an item to show up as an opportunity.</summary>
    public int MinimumGilPerDay { get; set; } = 1000;

    /// <summary>Draw the opportunity table with tighter row padding.</summary>
    public bool CompactRows { get; set; }

    /// <summary>Show the best gil/day in the server info bar.</summary>
    public bool DtrBar { get; set; } = true;

    /// <summary>Total gil/day from past scans, oldest first, for the header pulse.</summary>
    public List<ScanPulse> ScanHistory { get; set; } = new();

    public bool ShowDeadSlots { get; set; } = true;

    public bool WindowOpen { get; set; }

    public List<PinnedSlot> Pinned { get; set; } = new();

    /// <summary>Your own market listings, discovered while browsing the board.</summary>
    public List<CapturedListing> MyListings { get; set; } = new();

    /// <summary>Listings that vanished between checks, with the price they carried.</summary>
    public List<SoldEntry> SoldLog { get; set; } = new();

    /// <summary>Net worth window state and daily history.</summary>
    public bool WorthOpen { get; set; }

    public int WorthRefreshMinutes { get; set; } = 30;

    public WorthValuation WorthMode { get; set; } = WorthValuation.MinListing;

    public bool WorthCompact { get; set; }

    public List<WorthPoint> WorthHistory { get; set; } = new();

    /// <summary>Chat alerts for undercuts and price crashes.</summary>
    public bool AlertsEnabled { get; set; } = true;

    public int AlertIntervalMinutes { get; set; } = 15;

    public int AlertUndercutPercent { get; set; } = 2;

    public int AlertPriceDropPercent { get; set; } = 15;

    public int AlertCooldownMinutes { get; set; } = 360;

    /// <summary>Also surface alerts as a notification, so they do not scroll away in chat.</summary>
    public bool AlertToasts { get; set; } = true;

    /// <summary>Repricing: ignore moves smaller than this, and refuse big drops unless allowed.</summary>
    public int RepriceMinMoveGil { get; set; } = 0;

    /// <summary>Fill the asking price when the sell dialog opens, using the known market price.</summary>
    public bool AutoPriceSellDialog { get; set; }

    public int RepriceFloorPercent { get; set; } = 50;

    /// <summary>Pinned items crossing above this profit per day raise an alert after a scan.</summary>
    public long AlertMinProfitPerDay { get; set; } = 50_000;

    /// <summary>When each alert last fired, so a reload does not re-announce the same thing.</summary>
    public List<AlertCooldownEntry> AlertCooldowns { get; set; } = new();

    public string LastWorld { get; set; } = string.Empty;

    public DateTime? LastScanUtc { get; set; }

    public void Normalize()
    {
        this.TaxPercent = Math.Clamp(this.TaxPercent, 0f, 10f);
        this.DefaultStackSize = Math.Clamp(this.DefaultStackSize, 1, 99);
        this.PortfolioSlots = Math.Clamp(this.PortfolioSlots, 1, 60);
        this.PortfolioBudget = Math.Clamp(this.PortfolioBudget, 0, 999_999_999_999);
        this.DeadAfterDays = Math.Clamp(this.DeadAfterDays, 1, 365);
        this.MinimumVelocity = Math.Clamp(this.MinimumVelocity, 0f, 1000f);
        this.ShortlistSize = Math.Clamp(this.ShortlistSize, 20, 2000);
        this.CacheHours = Math.Clamp(this.CacheHours, 1, 168);
        this.BatchSize = Math.Clamp(this.BatchSize, 10, 100);
        this.MaxConcurrency = Math.Clamp(this.MaxConcurrency, 1, 8);
        this.MinimumGilPerDay = Math.Max(0, this.MinimumGilPerDay);
        this.Pinned ??= new List<PinnedSlot>();
        this.MyListings ??= new List<CapturedListing>();
        this.SoldLog ??= new List<SoldEntry>();
        this.WorthHistory ??= new List<WorthPoint>();
        this.WorthRefreshMinutes = Math.Clamp(this.WorthRefreshMinutes, 1, 240);

        if (this.WorthHistory.Count > 120)
            this.WorthHistory.RemoveRange(0, this.WorthHistory.Count - 120);

        this.SoldLog.RemoveAll(entry => entry.At < DateTime.UtcNow.AddDays(-14));

        if (this.SoldLog.Count > 200)
            this.SoldLog.RemoveRange(0, this.SoldLog.Count - 200);

        this.AlertIntervalMinutes = Math.Clamp(this.AlertIntervalMinutes, 1, 240);
        this.AlertUndercutPercent = Math.Clamp(this.AlertUndercutPercent, 0, 90);
        this.AlertPriceDropPercent = Math.Clamp(this.AlertPriceDropPercent, 1, 90);
        this.AlertCooldownMinutes = Math.Clamp(this.AlertCooldownMinutes, 1, 2880);
        this.AlertMinProfitPerDay = Math.Clamp(this.AlertMinProfitPerDay, 0, 1_000_000_000);
        this.RepriceMinMoveGil = Math.Clamp(this.RepriceMinMoveGil, 0, 1_000_000);
        this.RepriceFloorPercent = Math.Clamp(this.RepriceFloorPercent, 0, 100);
        this.AlertCooldowns ??= new List<AlertCooldownEntry>();

        var cooldownFloor = DateTime.UtcNow.AddDays(-7);
        this.AlertCooldowns.RemoveAll(entry => entry.RaisedUtc < cooldownFloor);

        if (this.AlertCooldowns.Count > 200)
            this.AlertCooldowns.RemoveRange(200, this.AlertCooldowns.Count - 200);

        if (this.MyListings.Count > ListingBook.MaxEntries)
            this.MyListings.RemoveRange(ListingBook.MaxEntries, this.MyListings.Count - ListingBook.MaxEntries);

        foreach (var slot in this.Pinned)
        {
            slot.StackSize = Math.Clamp(slot.StackSize, 1, 99);
        }

        this.WorldOverride ??= string.Empty;
        this.LastWorld ??= string.Empty;
    }

    public decimal TaxRate => (decimal)this.TaxPercent / 100m;
}
