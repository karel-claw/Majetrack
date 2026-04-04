namespace Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;

/// <summary>
/// Provides exchange rates between currency pairs.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>
    /// Returns the exchange rate to convert 1 unit of <paramref name="from"/> into <paramref name="to"/>.
    /// Returns null if the rate could not be determined (unknown currency, API unavailable, etc.).
    /// Returns 1 immediately when from == to.
    /// </summary>
    Task<decimal?> GetRateAsync(string from, string to, CancellationToken ct = default);
}
