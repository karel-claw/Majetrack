namespace Majetrack.Domain.CostBasis;

/// <summary>
/// Represents the open position produced by the FIFO cost-basis calculation.
/// Contains the remaining lots and aggregate statistics.
/// </summary>
public sealed class FifoCostBasisResult
{
    /// <summary>Total units still held in the open position.</summary>
    public decimal TotalQuantity { get; init; }

    /// <summary>Aggregate cost of all remaining units (sum of lot cost bases).</summary>
    public decimal TotalCostBasis { get; init; }

    /// <summary>
    /// Average cost per unit for the open position.
    /// Returns 0 when the position is fully closed.
    /// </summary>
    public decimal AverageCostPerUnit =>
        TotalQuantity == 0 ? 0m : TotalCostBasis / TotalQuantity;

    /// <summary>Individual open lots in FIFO order (oldest first).</summary>
    public IReadOnlyList<FifoCostBasisLot> OpenLots { get; init; } = Array.Empty<FifoCostBasisLot>();
}
