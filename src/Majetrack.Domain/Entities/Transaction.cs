using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

/// <summary>
/// Represents a single financial transaction recorded by the user, such as a buy, sell,
/// deposit, withdrawal, interest payment, or dividend. Transactions are the core building
/// blocks for calculating portfolio positions and performance.
/// </summary>
public class Transaction
{
    /// <summary>
    /// Unique internal identifier for the transaction.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The identifier of the user who owns this transaction.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The identifier of the asset involved in this transaction.
    /// Null for transaction types that are not tied to a specific asset,
    /// such as cash deposits or withdrawals.
    /// </summary>
    public Guid? AssetId { get; set; }

    /// <summary>
    /// Classifies the transaction (buy, sell, deposit, withdrawal, interest, or dividend),
    /// which determines how it affects portfolio positions and cash balances.
    /// </summary>
    public TransactionType TransactionType { get; set; }

    /// <summary>
    /// The date on which the transaction was executed or settled.
    /// </summary>
    public DateOnly TransactionDate { get; set; }

    /// <summary>
    /// The number of units involved in the transaction.
    /// For buys/sells this is the share or lot count; for cash transactions it is typically 1.
    /// Precision: 18,8.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// The price paid or received per single unit of the asset in the transaction currency.
    /// Precision: 18,8.
    /// </summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>
    /// The total monetary amount of the transaction in the original currency, including fees.
    /// Precision: 18,2.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// The currency in which the transaction was denominated.
    /// </summary>
    public Currency Currency { get; set; }

    /// <summary>
    /// The brokerage or investment platform where the transaction was executed.
    /// </summary>
    public Platform Platform { get; set; }

    /// <summary>
    /// Any fee or commission charged for the transaction, in the transaction currency.
    /// Precision: 18,2.
    /// </summary>
    public decimal Fee { get; set; }

    /// <summary>
    /// An optional user-provided note or description for the transaction.
    /// Null when no note has been added.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// An optional identifier from the external system (e.g. broker CSV export) that
    /// originated this transaction. Used for deduplication during import.
    /// Null for manually created transactions.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Timestamp indicating when the transaction record was first created in the system.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
