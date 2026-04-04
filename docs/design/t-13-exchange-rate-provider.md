# Design: T-13 IExchangeRateProvider + CnbExchangeRateProvider

## Summary

Czech National Bank (CNB) exchange rate provider. Provides daily FX rates from CNB API, with in-memory caching (24h TTL) and graceful fallback to previous business day.

## Feature Location

`src/Majetrack.Infrastructure/ExternalServices/CnbExchangeRateProvider/`

## Files to Create

| File | Purpose |
|------|---------|
| `IExchangeRateProvider.cs` | Interface definition |
| `CnbExchangeRateProvider.cs` | CNB API implementation |
| `CnbExchangeRateProviderOptions.cs` | Configuration options |
| `ExchangeRateCache.cs` | In-memory cache with TTL |

## Interface Contract

```csharp
public interface IExchangeRateProvider
{
    /// <summary>
    /// Gets exchange rate from one currency to another.
    /// Returns null if rate is unavailable.
    /// </summary>
    Task<decimal?> GetRateAsync(string from, string to, CancellationToken ct = default);
}
```

## CnbExchangeRateProvider Logic

### CNB API
- **URL:** `https://www.cnb.cz/en/financial_markets/foreign_exchange_processing/daily.txt`
- **Format:** Pipe-separated, first column CZK per unit
- **Update time:** ~14:30 CET daily

### Rate Calculation
- CNB provides rates as CZK per 1 unit (e.g., 1 EUR = 23.50 CZK)
- To get EUR → CZK: use direct rate
- To get CZK → EUR: calculate 1 / direct rate
- Cross pairs (EUR → USD): triangulate via CZK

### Fallback Logic
1. Try today's rate first
2. If weekend/holiday → loop back to previous business day
3. Max 7 days fallback, then return null

### Caching
- Single HTTP call loads all rates
- In-memory cache with 24h TTL
- SemaphoreSlim for thread-safe refresh

### Supported Pairs
| From | To | Status |
|------|-----|--------|
| CZK | EUR | ✅ |
| CZK | USD | ✅ |
| CZK | GBP | ✅ |
| CZK | CHF | ✅ |
| CZK | PLN | ✅ |
| EUR | CZK | ✅ (inverse) |
| USD | CZK | ✅ (inverse) |

## Data Model

None required — rates cached in-memory, not persisted.

## Configuration

```json
{
  "CnbExchangeRateProvider": {
    "CacheDuration": "24:00:00",
    "MaxFallbackDays": 7
  }
}
```

## Open Questions

1. **Currency Type Dependency:** Uses `string` to avoid tight coupling with Domain enum. T-12 uses Domain.Currency.
2. **Cache Persistence:** If app restarts, cache is lost. Acceptable for v1.
3. **Cross-pair precision:** EUR→USD triangulated via CZK has compounding rounding. Acceptable for v1.
4. **Staleness handling:** If CNB down, returns null (caller decides retry or fail).

## Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|----------|
| Cache location | In-memory | Simple, fast, stateless |
| Currency type | string | Decoupled from Domain enum |
| HTTP client | HttpClient | Typed, testable |
| Thread safety | SemaphoreSlim | Simple lock pattern |

---

_Date: 2026-04-04_