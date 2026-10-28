using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace SlotYield.Market;

/// <summary>One buyable entry currently loaded in the market board's item search proxy.</summary>
public readonly record struct BuyableListing(uint UnitPrice, uint Quantity, bool Hq);

/// <summary>
/// Thin wrapper over the game's market board item-search proxy, which is the same data the board
/// UI uses. Reading it is safe; buying is only ever done after the caller has validated the price.
/// </summary>
public static unsafe class MarketBoardBuyer
{
    public static InfoProxyItemSearch* Proxy()
    {
        var module = InfoModule.Instance();
        if (module is null)
            return null;

        return (InfoProxyItemSearch*)module->GetInfoProxyById(InfoProxyId.ItemSearch);
    }

    /// <summary>Ask the server for the listings of one item.</summary>
    public static bool RequestItem(uint itemId)
    {
        var proxy = Proxy();
        if (proxy is null)
            return false;

        proxy->SearchItemId = itemId;
        ((InfoProxyInterface*)proxy)->RequestData();
        return true;
    }

    public static bool WaitingForListings()
    {
        var proxy = Proxy();
        return proxy is not null && proxy->WaitingForListings;
    }

    public static List<BuyableListing> ReadListings(uint itemId)
    {
        var result = new List<BuyableListing>();
        var proxy = Proxy();
        if (proxy is null)
            return result;

        var count = proxy->ListingCount;
        var listings = proxy->Listings;

        for (var index = 0; index < listings.Length; index++)
        {
            if (index >= count)
                break;

            ref var entry = ref listings[index];
            if (entry.ItemId != itemId || entry.UnitPrice == 0 || entry.Quantity == 0)
                continue;

            result.Add(new BuyableListing(entry.UnitPrice, entry.Quantity, entry.IsHqItem));
        }

        return result;
    }

    /// <summary>Buy the matching listing, capped at <paramref name="quantity"/> units.</summary>
    public static bool Buy(BuyableListing listing, int quantity)
    {
        var proxy = Proxy();
        if (proxy is null)
            return false;

        var count = proxy->ListingCount;

        fixed (MarketBoardListing* entries = proxy->Listings)
        {
            for (var index = 0; index < count; index++)
            {
                var entry = entries + index;
                if (entry->ItemId == 0
                    || entry->UnitPrice != listing.UnitPrice
                    || entry->Quantity != listing.Quantity
                    || entry->IsHqItem != listing.Hq)
                {
                    continue;
                }

                if (!proxy->SetLastPurchasedItem(entry))
                    return false;

                proxy->LastPurchasedMarketboardItem.Quantity = (uint)Math.Clamp(quantity, 1, (int)entry->Quantity);
                return proxy->SendPurchaseRequestPacket();
            }
        }

        return false;
    }
}
