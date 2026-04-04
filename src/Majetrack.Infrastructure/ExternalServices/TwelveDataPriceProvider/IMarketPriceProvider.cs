namespace Majetrack.Infrastructure.ExternalServices.TwelveDataPriceProvider;

/// <summary>
/// Provides real-time or near-real-time market prices for financial instruments.
/// </summary>
public interface IMarketPriceProvider
{
    /// <summary>
    /// Returns the current price for the given symbol (e.g. "AAPL", "BTC/USD").
    /// Returns null if the symbol is unknown, the API is unavailable, or the response is invalid.
    /// </summary>
    Task<decimal?> GetPriceAsync(string symbol, CancellationToken ct = default);
}
