using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

/// <summary>
/// Stores a historical exchange rate between two currencies for a specific date.
/// Used to convert transaction amounts and portfolio values into a common reporting currency.
/// </summary>
public class ExchangeRate
{
    /// <summary>
    /// Unique internal identifier for the exchange rate record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The date for which this exchange rate is valid.
    /// Rates are stored as daily snapshots.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// The currency being converted from (the base currency).
    /// </summary>
    public Currency SourceCurrency { get; set; }

    /// <summary>
    /// The currency being converted to (the quote currency).
    /// </summary>
    public Currency TargetCurrency { get; set; }

    /// <summary>
    /// The conversion rate: one unit of <see cref="SourceCurrency"/> equals this many units
    /// of <see cref="TargetCurrency"/>. Precision: 18,8.
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// Timestamp indicating when this exchange rate was fetched from the external data source.
    /// </summary>
    public DateTimeOffset FetchedAt { get; set; }
}
