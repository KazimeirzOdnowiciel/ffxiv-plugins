using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mountbook.Services;

/// <summary>
/// What the market says about one mount's whistle: the cheapest asking price on the player's data
/// center, how many listings there are, and which world the cheapest one sits on.
/// </summary>
/// <param name="MinPrice">The cheapest price per unit, in gil.</param>
/// <param name="ListingsCount">How many listings the data center has.</param>
/// <param name="AveragePrice">The current average asking price, in gil.</param>
/// <param name="Location">The data center (or world) the answer names, empty when it names neither.</param>
/// <param name="CheapestWorld">The world carrying the cheapest listing, or empty when the API did not say.</param>
/// <param name="LastUploadTimeUnix">When a seller's retainer last reported a price, in unix seconds.</param>
public sealed record MarketQuote(
    int MinPrice,
    int ListingsCount,
    double AveragePrice,
    string Location,
    string CheapestWorld,
    long LastUploadTimeUnix);

/// <summary>
/// The Universalist API's per-item quote: building the request, reading the answer, and saying it in
/// one line.
/// </summary>
/// <remarks>
/// <para>
/// The request is made for the player's <em>data center</em>, not their world: a world-only answer hides
/// every cheaper listing one hop away, and cross-world travel makes those listings buyable. The API
/// takes a DC name, a world name or a region on the same path, so this is a naming decision and not a
/// different endpoint.
/// </para>
/// <para>
/// Pure and Dalamud-free (T1): the HTTP call lives in <c>Game/MarketPrices</c>, which runs it off the
/// draw thread and hands the body here. N2 allows exactly this one runtime request — a price cannot
/// ship with the plugin — so nothing here is allowed to run on the render path.
/// </para>
/// <para>
/// The API answers <c>hasData: false</c> for an item nobody lists, which is a real answer ("nobody is
/// selling this") and not an error; an item id that is not marketable at all (untradeable whistles)
/// answers the same way, which is why the caller only offers the button for a recorded-tradeable mount.
/// </para>
/// </remarks>
public static class MountMarket
{
    /// <summary>
    /// The endpoint one item's quote is read from. <paramref name="location"/> is a data center name
    /// (<c>Light</c>), a world name, or a region — the API accepts all three on the same path.
    /// </summary>
    /// <param name="location">The data center or world to ask about.</param>
    /// <param name="itemId">The whistle's item id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is null.</exception>
    public static string Url(string location, uint itemId)
    {
        ArgumentNullException.ThrowIfNull(location);

        return string.Concat(
            "https://universalis.app/api/v2/",
            Uri.EscapeDataString(location),
            "/",
            itemId.ToString(CultureInfo.InvariantCulture),
            "?listings=1");
    }

    /// <summary>The most item ids one batch request may carry; the API rejects more.</summary>
    public const int MaxBatchIds = 100;

    /// <summary>
    /// The endpoint several items' prices are read from in one request, so ordering the list by price
    /// costs one round trip instead of one per row. <c>listings=0</c> asks for the summary (the cheapest
    /// price) and not the listings themselves: the shelf needs a number, and seventy-three mounts' worth
    /// of listings is megabytes for a sort key.
    /// </summary>
    /// <param name="location">The data center or world to ask about.</param>
    /// <param name="itemIds">Between one and <see cref="MaxBatchIds"/> item ids.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentException">The id list is empty or too long.</exception>
    public static string BatchUrl(string location, IReadOnlyList<uint> itemIds)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            throw new ArgumentException("A batch needs at least one item id.", nameof(itemIds));
        }

        if (itemIds.Count > MaxBatchIds)
        {
            throw new ArgumentException(
                string.Concat("A batch may carry at most ", MaxBatchIds.ToString(CultureInfo.InvariantCulture), " item ids."),
                nameof(itemIds));
        }

        var ids = new StringBuilder(itemIds.Count * 7);

        for (var index = 0; index < itemIds.Count; index++)
        {
            if (index > 0)
            {
                ids.Append(',');
            }

            ids.Append(itemIds[index].ToString(CultureInfo.InvariantCulture));
        }

        return string.Concat(
            "https://universalis.app/api/v2/",
            Uri.EscapeDataString(location),
            "/",
            ids.ToString(),
            "?listings=0");
    }

    /// <summary>
    /// Reads a batch answer into <paramref name="prices"/>, keyed by item id: a listed item becomes its
    /// cheapest price, an item nobody lists becomes 0 (asked, nothing for sale), and an id the API does
    /// not know is left out, so "no sellers" and "never asked" stay different answers.
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <param name="prices">The dictionary the answer is written into.</param>
    /// <returns>How many ids the answer resolved to an item.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public static int ParseBatch(string json, IDictionary<uint, int> prices)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(prices);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var resolved = 0;

        foreach (JsonProperty property in items.EnumerateObject())
        {
            if (!uint.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out uint itemId))
            {
                continue;
            }

            resolved++;

            JsonElement item = property.Value;
            var price = 0;

            if (item.ValueKind == JsonValueKind.Object
                && (!item.TryGetProperty("hasData", out JsonElement hasData)
                    || hasData.ValueKind != JsonValueKind.False)
                && TryGetInt(item, "minPrice", out int min))
            {
                price = Math.Max(min, 0);
            }

            prices[itemId] = price;
        }

        return resolved;
    }

    /// <summary>
    /// Reads a quote out of the API's answer, or null when nobody is selling the item (or the answer
    /// carries no price at all).
    /// </summary>
    /// <param name="json">The response body.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    public static MarketQuote? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = document.RootElement;

        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("hasData", out System.Text.Json.JsonElement hasData)
            && hasData.ValueKind == System.Text.Json.JsonValueKind.False)
        {
            return null;
        }

        if (!TryGetInt(root, "minPrice", out int minPrice) || minPrice <= 0)
        {
            return null;
        }

        _ = TryGetInt(root, "listingsCount", out int listings);

        var average = 0d;

        if (root.TryGetProperty("averagePrice", out System.Text.Json.JsonElement averageValue)
            && averageValue.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            average = averageValue.GetDouble();
        }

        // A data-center answer names the DC and leaves the world fields null; a world answer does the
        // reverse. Reading both keeps one parser for either path.
        var location = ReadString(root, "dcName");

        if (location.Length == 0)
        {
            location = ReadString(root, "worldName");
        }

        // The cheapest listing still says which world it is on, which is the half a buyer needs.
        var cheapestWorld = string.Empty;

        if (root.TryGetProperty("listings", out System.Text.Json.JsonElement listingsArray)
            && listingsArray.ValueKind == System.Text.Json.JsonValueKind.Array
            && listingsArray.GetArrayLength() > 0)
        {
            cheapestWorld = ReadString(listingsArray[0], "worldName");
        }

        long upload = 0;

        if (root.TryGetProperty("lastUploadTime", out System.Text.Json.JsonElement uploadValue)
            && uploadValue.ValueKind == System.Text.Json.JsonValueKind.Number
            && uploadValue.TryGetInt64(out long milliseconds))
        {
            // The API writes milliseconds; everything else in this plugin speaks seconds.
            upload = milliseconds / 1000L;
        }

        return new MarketQuote(minPrice, listings, average, location, cheapestWorld, upload);
    }

    /// <summary>
    /// The one line the detail pane shows, e.g. <c>2,401,000 gil | 2 listings | Shiva (Light)</c>, or
    /// <c>Nobody is selling this on Light.</c> when the quote is null.
    /// </summary>
    /// <param name="quote">The quote, or null when nobody lists the item.</param>
    /// <param name="location">The data center that was asked about, used for the empty answer.</param>
    public static string Describe(MarketQuote? quote, string location)
    {
        if (quote is null)
        {
            return string.Concat("Nobody is selling this on ", location, ".");
        }

        var gil = quote.MinPrice.ToString("N0", CultureInfo.InvariantCulture);
        var listings = quote.ListingsCount == 1
            ? "1 listing"
            : string.Concat(quote.ListingsCount.ToString("N0", CultureInfo.InvariantCulture), " listings");
        var named = quote.Location.Length > 0 ? quote.Location : location;
        var where = quote.CheapestWorld.Length > 0
            ? string.Concat(quote.CheapestWorld, " (", named, ")")
            : named;

        // " | " and not a middle dot: the headless preview atlas has no U+00B7, and a golden would show
        // "?" where the game shows the separator.
        return string.Concat(gil, " gil | ", listings, " | ", where);
    }

    private static string ReadString(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out System.Text.Json.JsonElement value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    /// <summary>
    /// The cheapest price without the world, for a mount the batch read priced but the detail pane has
    /// not looked up on its own: <c>From 2,401,000 gil</c>. "From" because a batch answer carries no
    /// listing world, so the word says only as much as the answer does.
    /// </summary>
    /// <param name="price">The cheapest price in gil.</param>
    public static string DescribeFrom(int price) =>
        price > 0 ? string.Concat("From ", price.ToString("N0", CultureInfo.InvariantCulture), " gil") : string.Empty;

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;

        return root.TryGetProperty(name, out System.Text.Json.JsonElement element)
            && element.ValueKind == System.Text.Json.JsonValueKind.Number
            && element.TryGetInt32(out value);
    }
}
