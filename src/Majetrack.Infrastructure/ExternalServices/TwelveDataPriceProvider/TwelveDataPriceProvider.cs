using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Majetrack.Infrastructure.ExternalServices.TwelveDataPriceProvider;

/// <summary>
/// Fetches real-time market prices from the TwelveData API.
///
/// TwelveData /price endpoint returns JSON: {"price":"182.4500"} on success,
/// or {"code":400,"message":"...","status":"error"} when the symbol is unknown.
///
/// Prices are cached for 5 minutes (configurable) to avoid redundant API calls.
/// </summary>
public sealed class TwelveDataPriceProvider : IMarketPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TwelveDataPriceProvider> _logger;
    private readonly TwelveDataPriceProviderOptions _options;

    public TwelveDataPriceProvider(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<TwelveDataPriceProviderOptions> options,
        ILogger<TwelveDataPriceProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<decimal?> GetPriceAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        symbol = symbol.Trim().ToUpperInvariant();

        var cacheKey = $"twelvedata:price:{symbol}";
        if (_cache.TryGetValue(cacheKey, out decimal? cached))
            return cached;

        var price = await FetchPriceAsync(symbol, ct);
        if (price is not null)
            _cache.Set(cacheKey, price, _options.CacheTtl);

        return price;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private async Task<decimal?> FetchPriceAsync(string symbol, CancellationToken ct)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/price?symbol={Uri.EscapeDataString(symbol)}&apikey={_options.ApiKey}";
        _logger.LogDebug("TwelveData: fetching price for {Symbol}", symbol);

        string content;
        try
        {
            content = await _httpClient.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "TwelveData: HTTP request failed for {Symbol}", symbol);
            return null;
        }

        return ParsePriceResponse(content, symbol);
    }

    private decimal? ParsePriceResponse(string content, string symbol)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Error response: {"status":"error"} or {"code":400,...}
            if (root.TryGetProperty("status", out var status) && status.GetString() == "error")
            {
                _logger.LogDebug("TwelveData: symbol {Symbol} not found or error returned", symbol);
                return null;
            }

            if (root.TryGetProperty("code", out _))
            {
                _logger.LogDebug("TwelveData: error code returned for {Symbol}", symbol);
                return null;
            }

            // Success response: {"price":"182.4500"}
            if (root.TryGetProperty("price", out var priceElement))
            {
                var priceStr = priceElement.GetString();
                if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    return price;
            }

            _logger.LogWarning("TwelveData: unexpected response format for {Symbol}: {Content}", symbol, content);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "TwelveData: failed to parse response for {Symbol}", symbol);
            return null;
        }
    }
}
