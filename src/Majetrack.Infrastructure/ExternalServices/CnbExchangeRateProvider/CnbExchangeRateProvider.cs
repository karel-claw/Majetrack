using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;

/// <summary>
/// Fetches exchange rates from the Czech National Bank (CNB) daily fixing endpoint.
///
/// CNB publishes rates as CZK per N units of a foreign currency.
/// E.g. "USD|1|25.30" means 1 USD = 25.30 CZK.
///
/// Supported pairs:
///   - CZK ↔ foreign currency  (direct / inverse)
///   - foreign ↔ foreign        (cross-pair via CZK triangulation)
///   - same currency            (returns 1 immediately)
/// </summary>
public sealed class CnbExchangeRateProvider : IExchangeRateProvider, IDisposable
{
    // CNB format: "country|currency|amount|code|rate"
    // Example:   "Australia|dollar|1|AUD|14.879"
    private const string CzkCode = "CZK";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CnbExchangeRateProvider> _logger;
    private readonly CnbExchangeRateProviderOptions _options;

    // Semaphore per date-key to avoid thundering-herd on cache miss.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public CnbExchangeRateProvider(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<CnbExchangeRateProviderOptions> options,
        ILogger<CnbExchangeRateProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<decimal?> GetRateAsync(string from, string to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(from)) return null;
        if (string.IsNullOrWhiteSpace(to)) return null;

        from = from.Trim().ToUpperInvariant();
        to = to.Trim().ToUpperInvariant();

        if (from == to) return 1m;

        var rates = await GetRatesWithFallbackAsync(ct);
        if (rates is null) return null;

        return ComputeRate(rates, from, to);
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    private static decimal? ComputeRate(
        IReadOnlyDictionary<string, decimal> czkRates,
        string from,
        string to)
    {
        // "czkRates[X]" = how many CZK per 1 X

        if (from == CzkCode)
        {
            // CZK → foreign: inverse of rate
            return czkRates.TryGetValue(to, out var r) && r != 0 ? 1m / r : null;
        }

        if (to == CzkCode)
        {
            // foreign → CZK: direct rate
            return czkRates.TryGetValue(from, out var r) ? r : null;
        }

        // Cross pair: from → CZK → to
        if (!czkRates.TryGetValue(from, out var fromRate)) return null;
        if (!czkRates.TryGetValue(to, out var toRate)) return null;
        if (toRate == 0) return null;

        return fromRate / toRate;
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> GetRatesWithFallbackAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var i = 0; i <= _options.MaxFallbackDays; i++)
        {
            var date = today.AddDays(-i);

            // Skip weekends (CNB doesn't publish on weekends)
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var result = await GetRatesForDateAsync(date, ct);
            if (result is not null)
                return result;
        }

        _logger.LogError("CNB: could not fetch rates for any of the last {Days} business days.", _options.MaxFallbackDays);
        return null;
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> GetRatesForDateAsync(DateOnly date, CancellationToken ct)
    {
        var cacheKey = $"cnb:rates:{date:yyyy-MM-dd}";

        // Fast path: already cached
        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, decimal>? cached))
            return cached;

        // Slow path: fetch with per-key semaphore to avoid thundering herd
        var sem = _semaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        try
        {
            await sem.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("CNB: rate fetch cancelled for {Date}", date);
            return null;
        }

        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(cacheKey, out cached))
                return cached;

            var rates = await FetchFromCnbAsync(date, ct);
            if (rates is not null)
            {
                _cache.Set(cacheKey, rates, _options.CacheTtl);
            }

            return rates;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> FetchFromCnbAsync(DateOnly date, CancellationToken ct)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}?date={date:dd.MM.yyyy}";
        _logger.LogDebug("CNB: fetching rates for {Date} from {Url}", date, url);

        string content;
        try
        {
            content = await _httpClient.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "CNB: HTTP request failed for {Date}", date);
            return null;
        }

        return ParseCnbResponse(content, date);
    }

    /// <summary>
    /// Parses the CNB daily.txt format:
    ///   Line 1: date header  (e.g. "01 Apr 2025 #64")
    ///   Line 2: column names (e.g. "Country|Currency|Amount|Code|Rate")
    ///   Line 3+: data rows  (e.g. "Australia|dollar|1|AUD|14.879")
    /// Returns a dict of code → CZK per 1 unit.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal>? ParseCnbResponse(string content, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3) return null;

        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        for (var i = 2; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split('|');
            if (parts.Length < 5) continue;

            var code = parts[3].Trim().ToUpperInvariant();

            if (!decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                continue;

            if (!int.TryParse(parts[2].Trim(), out var amount) || amount <= 0)
                continue;

            // Normalise to 1 unit
            result[code] = rate / amount;
        }

        return result.Count > 0 ? result : null;
    }

    public void Dispose()
    {
        foreach (var sem in _semaphores.Values)
            sem.Dispose();
        _semaphores.Clear();
    }
}
