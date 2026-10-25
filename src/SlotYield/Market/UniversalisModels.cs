using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SlotYield.Market;

// Wire shapes for the two Universalis endpoints we use. Field names are matched
// case-insensitively, so the mixed camelCase of the API is fine.

public sealed class AggregatedResponse
{
    [JsonPropertyName("results")]
    public List<AggregatedItem>? Results { get; set; }

    [JsonPropertyName("failedItems")]
    public List<uint>? FailedItems { get; set; }
}

public sealed class AggregatedItem
{
    [JsonPropertyName("itemId")]
    public uint ItemId { get; set; }

    [JsonPropertyName("nq")]
    public AggregatedQuality? Nq { get; set; }

    [JsonPropertyName("hq")]
    public AggregatedQuality? Hq { get; set; }
}

public sealed class AggregatedQuality
{
    [JsonPropertyName("minListing")]
    public ListingPrices? MinListing { get; set; }

    [JsonPropertyName("averageSalePrice")]
    public SalePrices? AverageSalePrice { get; set; }

    [JsonPropertyName("dailySaleVelocity")]
    public Velocities? DailySaleVelocity { get; set; }
}

public sealed class ListingPrices
{
    [JsonPropertyName("world")]
    public WorldPrice? World { get; set; }

    [JsonPropertyName("dc")]
    public WorldPrice? Dc { get; set; }

    [JsonPropertyName("region")]
    public WorldPrice? Region { get; set; }
}

public sealed class WorldPrice
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("worldId")]
    public uint WorldId { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

public sealed class SalePrices
{
    [JsonPropertyName("world")]
    public SalePrice? World { get; set; }

    [JsonPropertyName("dc")]
    public SalePrice? Dc { get; set; }

    [JsonPropertyName("region")]
    public SalePrice? Region { get; set; }
}

public sealed class SalePrice
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }
}

public sealed class Velocities
{
    [JsonPropertyName("world")]
    public Velocity? World { get; set; }

    [JsonPropertyName("dc")]
    public Velocity? Dc { get; set; }

    [JsonPropertyName("region")]
    public Velocity? Region { get; set; }
}

public sealed class Velocity
{
    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }
}

// ---------------------------------------------------------------- quotes ----

public sealed class QuotesResponse
{
    [JsonPropertyName("itemIDs")]
    public List<uint>? ItemIds { get; set; }

    [JsonPropertyName("items")]
    public Dictionary<string, QuotedItem>? Items { get; set; }
}

public sealed class QuotedItem
{
    [JsonPropertyName("itemID")]
    public uint ItemId { get; set; }

    [JsonPropertyName("lastUploadTime")]
    public long LastUploadTime { get; set; }

    [JsonPropertyName("listings")]
    public List<Listing>? Listings { get; set; }

    [JsonPropertyName("recentHistory")]
    public List<Sale>? RecentHistory { get; set; }

    [JsonPropertyName("regularSaleVelocity")]
    public double RegularSaleVelocity { get; set; }

    [JsonPropertyName("dcName")]
    public string? DcName { get; set; }
}

public sealed class Listing
{
    [JsonPropertyName("pricePerUnit")]
    public decimal PricePerUnit { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("hq")]
    public bool Hq { get; set; }

    [JsonPropertyName("retainerName")]
    public string? RetainerName { get; set; }

    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    [JsonPropertyName("retainerCity")]
    public int RetainerCity { get; set; }

    [JsonPropertyName("tax")]
    public decimal Tax { get; set; }
}

public sealed class Sale
{
    [JsonPropertyName("pricePerUnit")]
    public decimal PricePerUnit { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("hq")]
    public bool Hq { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
