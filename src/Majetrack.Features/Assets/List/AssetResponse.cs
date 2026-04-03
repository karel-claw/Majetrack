namespace Majetrack.Features.Assets.List;

/// <summary>
/// Represents a single asset entry returned by GET /api/assets.
/// Contains the essential properties for displaying an asset in the catalogue.
/// </summary>
/// <param name="Id">Unique internal identifier for the asset.</param>
/// <param name="Ticker">The ticker or trading symbol of the asset (e.g. "AAPL"). Null for P2P loans.</param>
/// <param name="Name">Human-readable name of the asset (e.g. "Apple Inc.").</param>
/// <param name="AssetType">Classification of the asset: Stock, Etf, or P2pLoan.</param>
/// <param name="Exchange">The exchange where the asset is traded (e.g. "NASDAQ"). Null for P2P loans.</param>
/// <param name="Currency">The native currency in which the asset is denominated (ISO 4217: CZK, EUR, USD).</param>
/// <param name="Platform">The brokerage platform through which this asset is available: Xtb, Etoro, or Investown.</param>
public record AssetResponse(
    Guid Id,
    string? Ticker,
    string Name,
    string AssetType,
    string? Exchange,
    string Currency,
    string Platform
);
