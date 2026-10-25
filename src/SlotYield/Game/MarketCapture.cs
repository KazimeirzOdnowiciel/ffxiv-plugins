using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Network.Structures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using SlotYield.Core;

namespace SlotYield.Game;

/// <summary>
/// Captures the player's own market listings from two places:
///
/// 1. the retainer's sell list (AgentRetainer), which is where the player actually manages them;
/// 2. the market board (IMarketBoard), which supplies full listing data including quality when the
///    player browses an item they sell.
///
/// Both paths are read-only and ownership is decided by retainer id from ClientStructs.
/// </summary>
public sealed class MarketCapture
{
    private readonly Plugin plugin;

    // Board pages arrive in several packets; the page minimum is the lowest of all of them, not of
    // whichever packet landed last.
    private readonly Dictionary<(uint ItemId, bool Hq), (decimal Min, DateTime At)> boardMins = new();
    private bool sellListSeen;

    public MarketCapture(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Attach() => Plugin.MarketBoard.OfferingsReceived += this.OnOfferings;

    public void Detach() => Plugin.MarketBoard.OfferingsReceived -= this.OnOfferings;

    private void OnOfferings(IMarketBoardCurrentOfferings offerings)
    {
        try
        {
            this.CaptureBoardPrices(offerings);
            var mine = Retainers.All();
            if (mine.Count == 0)
                return;

            var byId = new Dictionary<ulong, RetainerInfo>(mine.Count);
            foreach (var retainer in mine)
                byId[retainer.Id] = retainer;

            var captured = 0;

            foreach (var listing in offerings.ItemListings)
            {
                if (!byId.TryGetValue(listing.RetainerId, out var retainer))
                    continue;

                var itemId = ListingBook.NormalizeItemId(listing.ItemId, out var hqEncoded);

                var entry = new CapturedListing(
                    itemId,
                    listing.IsHq || hqEncoded,
                    listing.PricePerUnit,
                    listing.ItemQuantity,
                    listing.ListingId,
                    listing.RetainerId,
                    retainer.Name,
                    listing.RetainerCityId,
                    DateTime.UtcNow);

                if (ListingBook.Upsert(this.plugin.Configuration.MyListings, entry))
                    captured++;
            }

            if (captured > 0)
            {
                this.plugin.MarkListingsDirty();
                Plugin.Log.Debug("[SlotYield] Captured {Count} of your listing(s) from the market board.", captured);
            }

            // Even when nothing changed, an unpriced book is worth one request.
            this.plugin.RequestListingQuotes(this.plugin.Configuration.MyListings.Select(entry => entry.ItemId));
        }
        catch (Exception ex)
        {
            // Never let a bad packet shape break the market board.
            Plugin.Log.Debug(ex, "[SlotYield] Market capture failed.");
        }
    }

    /// <summary>
    /// Mirrors the board page the player is looking at into the quote store. These carry no sale
    /// history, so they are price-only: enough to list at the market, not enough to claim velocity.
    /// </summary>
    private void CaptureBoardPrices(IMarketBoardCurrentOfferings offerings)
    {
        var now = DateTime.UtcNow;

        foreach (var hq in new[] { false, true })
        {
            var listings = offerings.ItemListings
                .Where(listing => listing.IsHq == hq && listing.PricePerUnit > 0)
                .ToList();

            if (listings.Count == 0)
                continue;

            var packetMin = listings.Min(listing => listing.PricePerUnit);
            var key = (listings[0].ItemId, hq);

            if (!this.boardMins.TryGetValue(key, out var seen) || now - seen.At > TimeSpan.FromSeconds(30))
                seen = (packetMin, now);
            else
                seen = (Math.Min(seen.Min, packetMin), now);

            this.boardMins[key] = seen;
            var min = seen.Min;

            var itemId = listings[0].ItemId;
            var quality = hq ? Quality.Hq : Quality.Nq;
            var previous = this.plugin.FindListingQuote(itemId, quality);

            var quote = new MarketQuote
            {
                ItemId = itemId,
                Quality = quality,
                MinListing = min,
                AverageSale = previous?.AverageSale > 0 ? previous.AverageSale : min,
                // A board page has prices but no sales history; keep whatever the last scan knew.
                DailyVelocity = previous?.DailyVelocity ?? 0,
                HistorySamples = previous?.HistorySamples ?? 0,
                PriceHistory = previous?.PriceHistory ?? Array.Empty<decimal>(),
                TrendPercent = previous?.TrendPercent ?? 0,
                ListingCount = listings.Count,
                DistinctSellers = listings.Select(listing => listing.RetainerId).Distinct().Count(),
                SellersAtMinPrice = listings
                    .Where(listing => listing.PricePerUnit == min)
                    .Select(listing => listing.RetainerId)
                    .Distinct()
                    .Count(),
                FetchedUtc = now,
                LastUploadUtc = now,
            };

            this.plugin.MergeListingQuotes(new[] { quote });
        }

        this.plugin.NotifyBoardPrices();
    }

    /// <summary>
    /// Reads the retainer's sell list. This is the window the player actually opens to manage
    /// listings, and it does not trigger any market board packet, which is why the packet path
    /// alone was not enough.
    /// </summary>
    public unsafe void PollRetainerSellList()
    {
        try
        {
            if (Plugin.GameGui.GetAddonByName("RetainerSellList", 1) == IntPtr.Zero)
            {
                this.sellListSeen = false;
                return;
            }

            var agent = AgentRetainer.Instance();
            if (agent is null || agent->SellListEntryCount <= 0)
                return;

            var manager = RetainerManager.Instance();
            if (manager is null || !manager->IsReady)
                return;

            var active = manager->GetActiveRetainer();
            if (active is null || active->RetainerId == 0)
                return;

            var retainerId = active->RetainerId;
            var retainerName = active->NameString;

            if (!this.sellListSeen)
            {
                this.sellListSeen = true;
                Plugin.Log.Information(
                    "[SlotYield] Retainer sell list open: {Count} entries on {Retainer}.",
                    agent->SellListEntryCount,
                    retainerName);
            }

            // Quality lives on the inventory item, not on the sell list entry.
            InventoryContainer* market = null;
            var inventory = InventoryManager.Instance();
            if (inventory is not null)
            {
                market = inventory->GetInventoryContainer(InventoryType.RetainerMarket);
                if (market is not null && !market->IsLoaded)
                    market = null;
            }

            var captured = 0;
            var live = new HashSet<(uint ItemId, bool Hq)>();
            var entries = agent->SellListEntries;

            // The span is a fixed-size buffer; only the first SellListEntryCount entries are live.
            // Reading past it picks up stale rows from an earlier retainer or session.
            var liveCount = Math.Min(entries.Length, Math.Max(0, agent->SellListEntryCount));

            for (var i = 0; i < liveCount; i++)
            {
                var entry = entries[i];
                if (entry.ItemId == 0 || entry.Quantity <= 0)
                    continue;

                var price = Money.ParseGil(entry.UnitPriceText.ToString());
                if (price <= 0)
                    continue;

                // HQ listings carry base id + 1,000,000 in this structure, exactly like inventory.
                var itemId = ListingBook.NormalizeItemId(entry.ItemId, out var hqEncoded);

                var hq = hqEncoded;
                if (market is not null && entry.InventorySlot < market->Size)
                {
                    var item = market->GetInventorySlot(entry.InventorySlot);
                    if (item is not null && item->GetBaseItemId() == itemId)
                        hq |= ((uint)item->Flags & 1u) != 0;
                }

                live.Add((itemId, hq));

                live.Add((itemId, hq));

                var listing = new CapturedListing(
                    itemId,
                    hq,
                    (uint)price,
                    (uint)Math.Max(0, entry.Quantity),
                    0,
                    retainerId,
                    retainerName,
                    0,
                    DateTime.UtcNow);

                if (ListingBook.Upsert(this.plugin.Configuration.MyListings, listing))
                    captured++;
            }

            // Anything the book still holds for this retainer but the list no longer shows was
            // withdrawn or sold, and must stop counting as listed.
            if (agent->SellListEntryCount > 0)
            {
                var gone = this.plugin.Configuration.MyListings
                    .Where(entry => entry.RetainerId == retainerId && !live.Contains((entry.ItemId, entry.Hq)))
                    .ToList();

                if (gone.Count > 0)
                {
                    foreach (var entry in gone)
                        this.plugin.RecordSale(entry);

                    this.plugin.Configuration.MyListings.RemoveAll(entry =>
                        entry.RetainerId == retainerId && !live.Contains((entry.ItemId, entry.Hq)));

                    this.plugin.MarkListingsDirty();
                    Plugin.Log.Information("[SlotYield] {Count} listing(s) gone from {Retainer}.", gone.Count, retainerName);
                }
            }

            if (captured > 0)
            {
                this.plugin.MarkListingsDirty();
                Plugin.Log.Debug(
                    "[SlotYield] Captured {Count} listing(s) from {Retainer} sell list.",
                    captured,
                    retainerName);
            }

            // The common case is that every row was already known from a previous session, so
            // nothing changes on the first open - but the prices still need fetching.
            this.plugin.RequestListingQuotes(this.plugin.Configuration.MyListings.Select(entry => entry.ItemId));
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[SlotYield] Retainer sell list capture failed.");
        }
    }

}
