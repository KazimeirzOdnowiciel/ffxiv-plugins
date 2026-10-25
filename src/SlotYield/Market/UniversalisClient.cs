using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlotYield.Market;

/// <summary>
/// Thin, polite wrapper around the two Universalis endpoints this plugin needs.
/// Concurrency-gated, retries transient failures, and counts requests so the UI can be honest.
/// </summary>
public sealed class UniversalisClient : IDisposable
{
    public const string DefaultBaseUrl = "https://universalis.app";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient http;
    private readonly SemaphoreSlim gate;
    private readonly string baseUrl;
    private long requestCount;

    public UniversalisClient(string baseUrl = DefaultBaseUrl, int maxConcurrency = 4)
    {
        this.baseUrl = baseUrl.TrimEnd('/');
        this.http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd("SlotYield/0.1 (+Dalamud plugin)");
        this.gate = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 8));
    }

    public int RequestCount => (int)Interlocked.Read(ref this.requestCount);

    public async Task<uint[]?> GetMarketableAsync(CancellationToken token)
        => await this.GetAsync<uint[]>($"{this.baseUrl}/api/v2/marketable", token).ConfigureAwait(false);

    public async Task<AggregatedResponse?> GetAggregatedAsync(string world, IReadOnlyList<uint> ids, CancellationToken token)
    {
        var url = AggregatedUrl(this.baseUrl, world, ids);
        return await this.GetAsync<AggregatedResponse>(url, token).ConfigureAwait(false);
    }

    public async Task<QuotesResponse?> GetQuotesAsync(string world, IReadOnlyList<uint> ids, int listings, int entries, CancellationToken token)
    {
        var url = QuotesUrl(this.baseUrl, world, ids, listings, entries);
        return await this.GetAsync<QuotesResponse>(url, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        this.http.Dispose();
        this.gate.Dispose();
    }

    public static string AggregatedUrl(string baseUrl, string world, IReadOnlyList<uint> ids) =>
        $"{baseUrl.TrimEnd('/')}/api/v2/aggregated/{Uri.EscapeDataString(world)}/{string.Join(',', ids)}";
    public static string QuotesUrl(string baseUrl, string world, IReadOnlyList<uint> ids, int listings, int entries) =>
        $"{baseUrl.TrimEnd('/')}/api/v2/{Uri.EscapeDataString(world)}/{string.Join(',', ids)}?listings={listings}&entries={entries}";

    private async Task<T?> GetAsync<T>(string url, CancellationToken token)
        where T : class
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            await this.gate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                Interlocked.Increment(ref this.requestCount);
                using var response = await this.http.GetAsync(url, token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    last = new HttpRequestException($"Universalis returned {(int)response.StatusCode}.");
                }
                else if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                else
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                last = ex;
            }
            finally
            {
                this.gate.Release();
            }

            if (attempt < 3)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), token).ConfigureAwait(false);
        }

        throw new MarketRequestException($"Universalis request failed: {url}", last);
    }
}

public sealed class MarketRequestException : Exception
{
    public MarketRequestException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
