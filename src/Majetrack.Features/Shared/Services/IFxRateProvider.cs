using Majetrack.Domain.Enums;

namespace Majetrack.Features.Shared.Services;

/// <summary>
/// Provides foreign exchange rate lookups for currency conversion.
/// Implementations may call external APIs or use cached rates.
/// </summary>
public interface IFxRateProvider
{
    /// <summary>
    /// Gets the exchange rate from <paramref name="from"/> to <paramref name="to"/>
    /// for the specified <paramref name="date"/>.
    /// </summary>
    /// <param name="from">The source currency.</param>
    /// <param name="to">The target currency.</param>
    /// <param name="date">The date for the rate lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The exchange rate on success, or <c>null</c> when the rate service is unavailable.
    /// </returns>
    Task<decimal?> GetRateAsync(Currency from, Currency to, DateOnly date, CancellationToken ct = default);
}
