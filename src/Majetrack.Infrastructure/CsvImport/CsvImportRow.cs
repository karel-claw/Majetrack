namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Represents a single normalised transaction row extracted from a platform CSV export.
/// All platform-specific column names and formats are mapped to this common model
/// before further processing (validation, persistence).
/// </summary>
public sealed class CsvImportRow
{
    /// <summary>
    /// The external identifier assigned by the platform (e.g. trade ID, order ID).
    /// May be <see langword="null"/> when the platform does not export an ID column.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// The type of transaction as a raw string from the CSV (e.g. "BUY", "SELL", "DIVIDEND").
    /// Downstream code is responsible for mapping this to <see cref="Majetrack.Domain.Enums.TransactionType"/>.
    /// </summary>
    public string? TransactionType { get; init; }

    /// <summary>
    /// The date on which the transaction was executed, as reported by the platform.
    /// </summary>
    public DateOnly TransactionDate { get; init; }

    /// <summary>
    /// The ticker or instrument symbol (e.g. "AAPL", "VWCE.DE").
    /// <see langword="null"/> for transactions that do not relate to a specific instrument,
    /// such as deposits or withdrawals.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// An optional human-readable description or comment exported by the platform.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// The profit or loss amount reported by the platform for this transaction, in <see cref="Currency"/>.
    /// May be <see langword="null"/> when not applicable (e.g. open position).
    /// </summary>
    public decimal? Profit { get; init; }

    /// <summary>
    /// The number of units (shares, lots, tokens) involved in the transaction.
    /// <see langword="null"/> for cash transactions such as deposits.
    /// </summary>
    public decimal? Volume { get; init; }

    /// <summary>
    /// The execution price per unit in <see cref="Currency"/>.
    /// <see langword="null"/> for transactions where unit price is not meaningful.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// The commission or fee charged by the platform for this transaction, in <see cref="Currency"/>.
    /// Stored as a positive value even when the platform exports it as negative.
    /// </summary>
    public decimal? Commission { get; init; }

    /// <summary>
    /// The overnight swap or rollover fee applied to leveraged positions, in <see cref="Currency"/>.
    /// Zero for non-leveraged positions.
    /// </summary>
    public decimal? Swap { get; init; }

    /// <summary>
    /// The ISO 4217 currency code for all monetary fields in this row (e.g. "USD", "EUR", "CZK").
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// The date on which the position or transaction was closed, if applicable.
    /// <see langword="null"/> for open positions or cash transactions.
    /// </summary>
    public DateOnly? ClosedDate { get; init; }
}
