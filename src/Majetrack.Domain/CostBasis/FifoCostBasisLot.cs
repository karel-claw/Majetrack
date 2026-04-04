namespace Majetrack.Domain.CostBasis;

/// <summary>
/// Represents a single open lot remaining after FIFO processing.
/// A lot corresponds to a Buy transaction (or a portion of one) that has not yet been matched
/// by a subsequent Sell.
/// </summary>
public sealed class FifoCostBasisLot
{
    /// <summary>The date on which the lot was purchased.</summary>
    public DateOnly PurchaseDate { get; init; }

    /// <summary>Number of units remaining in this lot.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Price paid per unit for this lot.</summary>
    public decimal PricePerUnit { get; init; }

    /// <summary>Total cost basis for the remaining units in this lot.</summary>
    public decimal TotalCost => Quantity * PricePerUnit;
}
