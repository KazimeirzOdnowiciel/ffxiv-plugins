using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Mountbook.Services;

namespace Mountbook.Game;

/// <summary>
/// Reads market prices from Universalis, off the draw thread: one item for the detail pane, one batch for
/// the table's Price column.
/// </summary>
/// <remarks>
/// <para>
/// N2 allows exactly one runtime request in this plugin and this is it: a price cannot ship with the
/// data, so the player asks for it. <see cref="Request"/> only starts a task; the answer arrives in
/// <see cref="TryTakeFinished"/> on a later frame, so no frame ever waits on a socket.
/// </para>
/// <para>
/// A quote is kept for <see cref="Fresh"/> and a "nobody is selling this" answer is cached as well, so
/// clicking the button twice does not spend a request re-reading the same emptiness. Failures are not
/// cached: a timeout is not an answer, and a network blip should be retryable.
/// </para>
/// </remarks>
internal sealed class MarketPrices : IDisposable
{
    /// <summary>How long a quote is trusted before the button re-reads it.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How many ids one batch request carries. The API allows 100, but a data-center-wide query is an
    /// aggregation over every world: measured on Light, 10 ids take 3.9 s, 25 take 9.7 s and anything
    /// larger answers 504 from the edge. Fifteen keeps a request near six seconds, and the read is split
    /// into chunks that each land a little more of the answer.
    /// </summary>
    private const int ChunkSize = 15;

    /// <summary>
    /// How many chunk requests may fail before the read gives up. A failed chunk is split in half and
    /// retried, so the budget is what stops a dead connection from turning into a hundred requests.
    /// </summary>
    private const int MaxBatchFailures = 8;

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private readonly IPluginLog log;

    private readonly Func<string> worldName;

    private readonly Dictionary<uint, MarketQuote?> cache = [];

    private readonly Dictionary<uint, DateTime> cachedAt = [];

    private readonly HashSet<uint> inFlight = [];

    private readonly List<(uint ItemId, MarketQuote? Quote, bool Failed)> finished = [];

    private readonly object gate = new();

    /// <summary>Item id to cheapest price from the last batch read; 0 means asked and nobody sells it.</summary>
    private readonly Dictionary<uint, int> prices = [];

    private DateTime pricesAt = DateTime.MinValue;

    private bool batchInFlight;

    private int priceVersion;

    private bool loggedFailure;

    public MarketPrices(IPluginLog log, Func<string> worldName)
    {
        this.log = log;
        this.worldName = worldName;

        // The API asks clients to identify themselves; an anonymous request is refused outright.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mountbook/0.1 (+https://github.com/KazimeirzOdnowiciel/Deepseek-Kingdom)");
    }

    /// <summary>True when a quote for <paramref name="itemId"/> is still fresh and may be shown as is.</summary>
    /// <param name="itemId">The whistle's item id.</param>
    /// <param name="quote">The cached quote, or null when the cache says nobody sells it.</param>
    public bool TryGetFresh(uint itemId, out MarketQuote? quote)
    {
        lock (gate)
        {
            quote = null;

            return cachedAt.TryGetValue(itemId, out DateTime at)
                && DateTime.UtcNow - at < Fresh
                && cache.TryGetValue(itemId, out quote);
        }
    }

    /// <summary>True while a request for <paramref name="itemId"/> is out.</summary>
    /// <param name="itemId">The whistle's item id.</param>
    public bool IsInFlight(uint itemId)
    {
        lock (gate)
        {
            return inFlight.Contains(itemId);
        }
    }

    /// <summary>
    /// Starts one request for <paramref name="itemId"/>. Never blocks, never throws at the caller, and
    /// does nothing when the same item is already being read or has a fresh quote.
    /// </summary>
    /// <param name="itemId">The whistle's item id.</param>
    /// <returns>True when this call started a request.</returns>
    public bool Request(uint itemId) => Request(itemId, force: false);

    /// <summary>
    /// Starts one request for <paramref name="itemId"/>, and with <paramref name="force"/> re-reads a quote
    /// that is still inside <see cref="Fresh"/>.
    /// </summary>
    /// <remarks>
    /// The watch poll is the one caller that forces: its interval is the schedule the player set up, and a
    /// cached quote is exactly the answer such a poll exists to replace. A request already out for the item
    /// is never doubled, forced or not, so two callers asking for the same whistle still cost one request.
    /// </remarks>
    /// <param name="itemId">The whistle's item id.</param>
    /// <param name="force">True to ignore a fresh cached quote.</param>
    /// <returns>
    /// True when this call started a request: false for an item already being read, for a fresh quote, and for
    /// no data center to ask about. The watch poll arms its interval on this answer, so a poll that could not
    /// ask must not report that it did.
    /// </returns>
    public bool Request(uint itemId, bool force)
    {
        if (itemId == 0)
        {
            return false;
        }

        string world = worldName();

        if (string.IsNullOrWhiteSpace(world))
        {
            return false;
        }

        lock (gate)
        {
            if (inFlight.Contains(itemId) || (!force && cachedAt.TryGetValue(itemId, out DateTime at)
                && DateTime.UtcNow - at < Fresh))
            {
                return false;
            }

            inFlight.Add(itemId);
        }

        _ = Task.Run(async () =>
        {
            MarketQuote? quote = null;
            var failed = false;

            try
            {
                string body = await http.GetStringAsync(MountMarket.Url(world, itemId)).ConfigureAwait(false);
                quote = MountMarket.Parse(body);
            }
            catch (Exception ex)
            {
                failed = true;
                ReportFailure(ex, itemId);
            }

            lock (gate)
            {
                inFlight.Remove(itemId);

                if (!failed)
                {
                    cache[itemId] = quote;
                    cachedAt[itemId] = DateTime.UtcNow;
                }

                finished.Add((itemId, quote, failed));
            }
        });

        return true;
    }

    /// <summary>True while a batch read is out.</summary>
    public bool BatchInFlight
    {
        get
        {
            lock (gate)
            {
                return batchInFlight;
            }
        }
    }

    /// <summary>
    /// True when the batch prices are still fresh enough to order the list by, so switching to the
    /// cheapest-first order does not spend a request every time the combo is touched.
    /// </summary>
    public bool HasFreshPrices()
    {
        lock (gate)
        {
            return prices.Count > 0 && DateTime.UtcNow - pricesAt < Fresh;
        }
    }

    /// <summary>
    /// Counts the complete price sets published so far. The window re-queries only when this changes, which
    /// is what puts a newly read price into the row order without re-sorting every frame.
    /// </summary>
    public int PriceVersion
    {
        get
        {
            lock (gate)
            {
                return priceVersion;
            }
        }
    }

    /// <summary>The cheapest known price for one item from the last batch read.</summary>
    /// <param name="itemId">The whistle's item id.</param>
    /// <param name="price">The price in gil, or 0 when nobody lists it.</param>
    /// <returns>True when the last batch asked about this item.</returns>
    public bool TryGetPrice(uint itemId, out int price)
    {
        lock (gate)
        {
            return prices.TryGetValue(itemId, out price);
        }
    }

    /// <summary>
    /// Reads the cheapest price of many items in as few requests as the API allows, so ordering the
    /// shelf by price costs one round trip rather than one per row. Never blocks, never throws at the
    /// caller; a chunk that fails leaves the items it covered unasked, so a later attempt can retry them.
    /// </summary>
    /// <remarks>
    /// The answer is published once, as one set, when the last chunk has landed - not one chunk at a time.
    /// The window rebuilds the list and the table's Price column from <see cref="PriceVersion"/>, so a set
    /// published per chunk drew a column that filled fifteen rows every six seconds and re-sorted the rows
    /// under the pointer five times over. Prices are also the one cell that can appear after a row is
    /// already drawn, which is exactly the state that must not be visible as an in-progress one: the column
    /// either has what the last complete read answered, or it has nothing yet and every row draws empty.
    /// A read where every chunk failed publishes nothing at all and leaves the previous set in place, so no
    /// row ever shows a number a later read has replaced with a partial one.
    /// </remarks>
    /// <param name="itemIds">The item ids to price.</param>
    public void RequestBatch(IReadOnlyList<uint> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return;
        }

        string location = worldName();

        if (string.IsNullOrWhiteSpace(location))
        {
            return;
        }

        lock (gate)
        {
            if (batchInFlight)
            {
                return;
            }

            batchInFlight = true;
        }

        var chunks = new List<uint[]>((itemIds.Count / ChunkSize) + 1);

        for (var start = 0; start < itemIds.Count; start += ChunkSize)
        {
            int size = Math.Min(ChunkSize, itemIds.Count - start);
            var chunk = new uint[size];

            for (var index = 0; index < size; index++)
            {
                chunk[index] = itemIds[start + index];
            }

            chunks.Add(chunk);
        }

        _ = Task.Run(async () =>
        {
            var total = 0;
            var requests = 0;
            var failures = 0;
            var pending = new Stack<uint[]>(chunks);

            // Staged, not published: a chunk's answer joins this set, and the set is swapped in once when
            // the last chunk has landed. See the remarks on this method for why it is not per chunk.
            var read = new Dictionary<uint, int>(itemIds.Count);

            while (pending.Count > 0)
            {
                uint[] chunk = pending.Pop();
                requests++;

                try
                {
                    string body = await http.GetStringAsync(MountMarket.BatchUrl(location, chunk))
                        .ConfigureAwait(false);

                    var answered = new Dictionary<uint, int>();
                    MountMarket.ParseBatch(body, answered);

                    lock (gate)
                    {
                        foreach (KeyValuePair<uint, int> pair in answered)
                        {
                            read[pair.Key] = pair.Value;
                        }

                        total += answered.Count;
                    }
                }
                catch (Exception ex) when (chunk.Length > 1 && failures < MaxBatchFailures)
                {
                    // A data-center query is an aggregation over every world, so how long a chunk takes
                    // depends on how busy the service is: the edge answered 504 for one chunk of fifteen
                    // while the other four came back. Splitting the failed chunk is what adapts to that
                    // instead of losing fifteen mounts' prices.
                    failures++;

                    int half = chunk.Length / 2;
                    var left = new uint[half];
                    var right = new uint[chunk.Length - half];
                    Array.Copy(chunk, 0, left, 0, half);
                    Array.Copy(chunk, half, right, 0, right.Length);
                    pending.Push(left);
                    pending.Push(right);

                    log.Debug(
                        "Market: a chunk of {Count} failed ({Reason}); retrying as {Left}+{Right}.",
                        chunk.Length,
                        ex.GetType().Name,
                        left.Length,
                        right.Length);
                }
                catch (Exception ex)
                {
                    failures++;
                    ReportFailure(ex, chunk[0]);
                }
            }

            lock (gate)
            {
                // One publish for the whole read, and only if it answered anything: a read that failed
                // everywhere leaves the previous set (and its version) untouched, so nothing the table is
                // showing changes and the next attempt is still due.
                if (read.Count > 0)
                {
                    foreach (KeyValuePair<uint, int> pair in read)
                    {
                        prices[pair.Key] = pair.Value;
                    }

                    pricesAt = DateTime.UtcNow;
                    priceVersion++;
                }

                batchInFlight = false;
            }

            // Information, not Debug: one line per user action, and the only way to check that the
            // cheapest-first order asked for prices at all without reading the table row order.
            log.Information(
                "Market: {Count} prices read across {Requests} request(s).",
                total,
                requests);
        });
    }

    /// <summary>
    /// Takes one finished request, oldest first, so the window can put the answer on the state it draws.
    /// </summary>
    /// <param name="itemId">The item the answer belongs to.</param>
    /// <param name="quote">The quote, or null when nobody is selling it.</param>
    /// <param name="failed">True when the request itself failed (a timeout, a refusal).</param>
    /// <returns>True when an answer was waiting.</returns>
    public bool TryTakeFinished(out uint itemId, out MarketQuote? quote, out bool failed)
    {
        lock (gate)
        {
            if (finished.Count == 0)
            {
                itemId = 0;
                quote = null;
                failed = false;

                return false;
            }

            (itemId, quote, failed) = finished[0];
            finished.RemoveAt(0);

            return true;
        }
    }

    public void Dispose() => http.Dispose();

    /// <summary>Logs the first failure only: a dead connection must not fill the log every click (N4).</summary>
    private void ReportFailure(Exception ex, uint itemId)
    {
        if (loggedFailure)
        {
            return;
        }

        loggedFailure = true;
        log.Error(ex, "Could not read the market price for item {ItemId}.", itemId);
    }
}
