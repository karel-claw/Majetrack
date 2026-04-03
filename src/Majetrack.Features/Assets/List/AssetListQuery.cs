using Majetrack.Domain.Enums;

namespace Majetrack.Features.Assets.List;

/// <summary>
/// Query parameters for filtering the asset list.
/// Both filters are optional; omitting them returns all assets.
/// Bound from query string via [AsParameters] attribute on the handler.
/// </summary>
public record AssetListQuery
{
    /// <summary>
    /// The trading platform to filter by (Xtb, Etoro, or Investown).
    /// Null means no platform filter is applied.
    /// </summary>
    public Platform? Platform { get; init; }

    /// <summary>
    /// The asset type to filter by (Stock, Etf, or P2pLoan).
    /// Null means no asset type filter is applied.
    /// </summary>
    public AssetType? AssetType { get; init; }
}
