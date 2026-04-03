namespace Majetrack.Domain.Enums;

/// <summary>
/// Represents the supported currencies used for transactions, asset pricing,
/// and portfolio valuation. Exchange rates between these currencies are stored
/// for cross-currency reporting. Values follow ISO 4217 naming conventions.
/// </summary>
public enum Currency
{
    /// <summary>
    /// Czech koruna (CZK) — the primary reporting currency for the portfolio,
    /// as the user is based in the Czech Republic.
    /// </summary>
    CZK = 1,

    /// <summary>
    /// Euro (EUR) — used for European-listed assets and platforms
    /// such as XTB European markets.
    /// </summary>
    EUR = 2,

    /// <summary>
    /// United States dollar (USD) — used for US-listed equities and ETFs.
    /// </summary>
    USD = 3,
}
