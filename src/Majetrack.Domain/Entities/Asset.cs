using Majetrack.Domain.Enums;

namespace Majetrack.Domain.Entities;

/// <summary>
/// Represents a financial instrument (stock, ETF, or P2P loan) that can be traded or held
/// within the user's portfolio. Assets are shared across users and linked to transactions.
/// </summary>
public class Asset
{
    /// <summary>
    /// Unique internal identifier for the asset.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The ticker or trading symbol of the asset (e.g. "AAPL", "VWCE.DE").
    /// Null for asset types that do not have a ticker, such as P2P loans.
    /// </summary>
    public string? Ticker { get; set; }

    /// <summary>
    /// Human-readable name of the asset (e.g. "Apple Inc.", "Vanguard FTSE All-World ETF").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Classifies the asset as a stock, ETF, or P2P loan, which affects how
    /// positions and gains are calculated.
    /// </summary>
    public AssetType AssetType { get; set; }

    /// <summary>
    /// The exchange or marketplace where the asset is traded (e.g. "NASDAQ", "XETRA").
    /// Null for assets not listed on a traditional exchange, such as P2P loans.
    /// </summary>
    public string? Exchange { get; set; }

    /// <summary>
    /// The native currency in which the asset is denominated and priced.
    /// </summary>
    public Currency Currency { get; set; }

    /// <summary>
    /// The brokerage or investment platform through which this asset is available.
    /// </summary>
    public Platform Platform { get; set; }

    /// <summary>
    /// Timestamp indicating when the asset record was first created in the system.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
