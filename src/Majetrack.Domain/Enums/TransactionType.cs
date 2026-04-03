namespace Majetrack.Domain.Enums;

/// <summary>
/// Classifies the type of a financial transaction, determining how it affects
/// portfolio positions, cost basis calculations, and cash balances.
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Purchase of an asset, which increases the position quantity and establishes
    /// cost basis for future gain/loss calculations.
    /// </summary>
    Buy = 1,

    /// <summary>
    /// Sale of a previously held asset, which reduces the position quantity and
    /// triggers realized gain/loss calculation using the FIFO method.
    /// </summary>
    Sell = 2,

    /// <summary>
    /// Cash deposit into a brokerage or investment account, increasing the
    /// available cash balance on the platform.
    /// </summary>
    Deposit = 3,

    /// <summary>
    /// Cash withdrawal from a brokerage or investment account, reducing the
    /// available cash balance on the platform.
    /// </summary>
    Withdrawal = 4,

    /// <summary>
    /// Interest income received, typically from P2P lending platforms or
    /// cash holdings. Treated as realized income.
    /// </summary>
    Interest = 5,

    /// <summary>
    /// Dividend payment received from a held stock or ETF position.
    /// Treated as realized income and may be subject to withholding tax.
    /// </summary>
    Dividend = 6,
}
