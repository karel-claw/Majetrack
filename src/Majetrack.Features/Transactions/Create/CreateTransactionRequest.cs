namespace Majetrack.Features.Transactions.Create;

/// <summary>
/// Represents the JSON request body for creating a new transaction.
/// Conditional fields (<see cref="AssetId"/>, <see cref="Quantity"/>, <see cref="PricePerUnit"/>)
/// are required for Buy, Sell, Interest, and Dividend transaction types,
/// but omitted for Deposit and Withdrawal.
/// </summary>
/// <param name="TransactionType">
/// The type of transaction: Buy, Sell, Deposit, Withdrawal, Interest, or Dividend.
/// </param>
/// <param name="TransactionDate">The date the transaction was executed (ISO 8601 date).</param>
/// <param name="TotalAmount">The total monetary amount of the transaction.</param>
/// <param name="Currency">The currency code (CZK, EUR, USD).</param>
/// <param name="Platform">The brokerage platform (Xtb, Etoro, Investown).</param>
/// <param name="AssetId">
/// The identifier of the asset involved. Required for Buy, Sell, Interest, Dividend.
/// Must be null or omitted for Deposit and Withdrawal.
/// </param>
/// <param name="Quantity">
/// The number of units transacted. Required for Buy, Sell, Interest, Dividend.
/// </param>
/// <param name="PricePerUnit">
/// The price per unit in the transaction currency. Required for Buy, Sell, Interest, Dividend.
/// </param>
/// <param name="Fee">Transaction fee or commission. Defaults to 0 when omitted.</param>
/// <param name="Note">An optional user-provided note for the transaction.</param>
public record CreateTransactionRequest(
    string? TransactionType,
    string? TransactionDate,
    decimal? TotalAmount,
    string? Currency,
    string? Platform,
    Guid? AssetId,
    decimal? Quantity,
    decimal? PricePerUnit,
    decimal? Fee,
    string? Note);
