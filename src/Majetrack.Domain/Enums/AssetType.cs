namespace Majetrack.Domain.Enums;

/// <summary>
/// Classifies the type of financial instrument, which determines how positions
/// are tracked, how gains are calculated, and which import logic applies.
/// </summary>
public enum AssetType
{
    /// <summary>
    /// An individual company stock (equity share). Positions are tracked by
    /// share count and valued at market price.
    /// </summary>
    Stock = 1,

    /// <summary>
    /// An exchange-traded fund that tracks an index, sector, or asset class.
    /// Treated similarly to stocks for position and gain calculations.
    /// </summary>
    Etf = 2,

    /// <summary>
    /// A peer-to-peer loan investment, typically on platforms like Investown.
    /// Returns are generated through interest payments rather than price appreciation.
    /// </summary>
    P2pLoan = 3,
}
