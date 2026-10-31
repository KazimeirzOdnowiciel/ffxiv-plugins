using System;
using System.Collections.Generic;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The Universalis quote: what is read out of the answer, and how it is said in one line. The samples
/// below are real responses for a tradeable whistle (item 23023), trimmed to the fields the reader uses —
/// one asked by data center, which is what the plugin asks for, and one by world, which the parser still
/// accepts.
/// </summary>
public sealed class MountMarketTests
{
    private const string DataCenterSample = """
        {
          "itemID": 23023,
          "lastUploadTime": 1793450811428,
          "dcName": "Light",
          "regionName": null,
          "worldID": null,
          "worldName": null,
          "averagePrice": 2643822.8,
          "minPrice": 2401000,
          "listingsCount": 2,
          "hasData": true,
          "listings": [ { "pricePerUnit": 2401000, "quantity": 1, "worldName": "Shiva", "worldID": 67 } ]
        }
        """;

    private const string WorldSample = """
        {
          "itemID": 23023,
          "worldID": 56,
          "lastUploadTime": 1793450811428,
          "minPrice": 3493999,
          "listingsCount": 2,
          "averagePrice": 3676999.5,
          "worldName": "Phoenix",
          "hasData": true
        }
        """;

    [Fact]
    public void ParseReadsTheCheapestPriceTheCountAndTheDataCenter()
    {
        MarketQuote? quote = MountMarket.Parse(DataCenterSample);

        Assert.NotNull(quote);
        Assert.Equal(2401000, quote!.MinPrice);
        Assert.Equal(2, quote.ListingsCount);
        Assert.Equal("Light", quote.Location);
        Assert.Equal("Shiva", quote.CheapestWorld);
        Assert.Equal(2643822.8, quote.AveragePrice);
    }

    [Fact]
    public void AWorldShapedAnswerStillParses()
    {
        // One parser for either path: a world answer names the world and carries no listing worlds.
        MarketQuote? quote = MountMarket.Parse(WorldSample);

        Assert.NotNull(quote);
        Assert.Equal("Phoenix", quote!.Location);
        Assert.Equal(string.Empty, quote.CheapestWorld);
    }

    [Fact]
    public void TheUploadTimeIsConvertedFromMilliseconds()
    {
        // The API writes milliseconds; the rest of the plugin speaks seconds.
        Assert.Equal(1793450811L, MountMarket.Parse(DataCenterSample)!.LastUploadTimeUnix);
    }

    [Fact]
    public void NoDataMeansNobodyIsSellingIt()
    {
        const string Empty = """{ "itemID": 23023, "hasData": false, "minPrice": 0, "listingsCount": 0 }""";

        Assert.Null(MountMarket.Parse(Empty));
    }

    [Fact]
    public void AQuoteWithNoPriceIsNotAQuote()
    {
        // An answer that carries no minPrice at all (an item id the market has never seen) must read as
        // "nobody is selling", not as "0 gil".
        const string NoPrice = """{ "dcName": "Light", "hasData": true, "listingsCount": 0 }""";
        const string ZeroPrice = """{ "dcName": "Light", "hasData": true, "minPrice": 0, "listingsCount": 0 }""";

        Assert.Null(MountMarket.Parse(NoPrice));
        Assert.Null(MountMarket.Parse(ZeroPrice));
    }

    [Fact]
    public void DescribeNamesTheWorldAndTheDataCenter()
    {
        var quote = new MarketQuote(2401000, 2, 2643822.8, "Light", "Shiva", 1793450811L);

        Assert.Equal("2,401,000 gil | 2 listings | Shiva (Light)", MountMarket.Describe(quote, "Light"));
    }

    [Fact]
    public void DescribeFallsBackToTheDataCenterWhenTheListingWorldIsUnknown()
    {
        var quote = new MarketQuote(1500, 1, 1500, "Light", string.Empty, 0);

        Assert.Equal("1,500 gil | 1 listing | Light", MountMarket.Describe(quote, "Light"));
    }

    [Fact]
    public void DescribeSaysWhenNobodySellsIt() =>
        Assert.Equal("Nobody is selling this on Light.", MountMarket.Describe(null, "Light"));

    [Fact]
    public void UrlPointsAtTheDataCenterAndTheItem()
    {
        Assert.Equal(
            "https://universalis.app/api/v2/Light/23023?listings=1",
            MountMarket.Url("Light", 23023u));
    }

    [Fact]
    public void UrlEscapesTheLocationName() =>
        Assert.Equal(
            "https://universalis.app/api/v2/Siren%20Prime/1?listings=1",
            MountMarket.Url("Siren Prime", 1u));

    [Fact]
    public void BatchUrlAsksForOneSummaryOfEveryId()
    {
        Assert.Equal(
            "https://universalis.app/api/v2/Light/23023,50438,16561?listings=0",
            MountMarket.BatchUrl("Light", [23023u, 50438u, 16561u]));
    }

    [Fact]
    public void BatchUrlRefusesAnEmptyOrOversizedList()
    {
        Assert.Throws<ArgumentException>(() => MountMarket.BatchUrl("Light", []));
        Assert.Throws<ArgumentException>(
            () => MountMarket.BatchUrl("Light", new uint[MountMarket.MaxBatchIds + 1]));
    }

    [Fact]
    public void ParseBatchReadsPricesAndKeepsNoSellersAsZero()
    {
        // A real batch answer, trimmed. 16561 is in the API's unresolved list, so it never appears in
        // items at all - which is the difference between "asked and nobody sells it" (0) and "never
        // asked" (no key).
        const string Batch = """
            {
              "itemIDs": [50438, 39490, 16561],
              "dcName": "Light",
              "unresolvedItems": [16561],
              "items": {
                "50438": { "itemID": 50438, "minPrice": 36993090, "hasData": true },
                "39490": { "itemID": 39490, "minPrice": 0, "hasData": false }
              }
            }
            """;

        var prices = new Dictionary<uint, int>();

        Assert.Equal(2, MountMarket.ParseBatch(Batch, prices));
        Assert.Equal(36993090, prices[50438u]);
        Assert.Equal(0, prices[39490u]);
        Assert.False(prices.ContainsKey(16561u));
    }

    [Fact]
    public void ParseBatchIgnoresAnAnswerWithoutItems()
    {
        var prices = new Dictionary<uint, int>();

        Assert.Equal(0, MountMarket.ParseBatch("""{ "unresolvedItems": [1] }""", prices));
        Assert.Empty(prices);
    }

    [Fact]
    public void DescribeFromIsUsedForAPriceWithoutAWorld()
    {
        // A batch answer names the data center but not the listing's world, so the line says only what
        // it knows.
        Assert.Equal("From 2,401,000 gil", MountMarket.DescribeFrom(2401000));
        Assert.Equal(string.Empty, MountMarket.DescribeFrom(0));
    }
}
