using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;

namespace Majetrack.Domain.CostBasis;

/// <summary>
/// Calculates the open portfolio position for a single asset using the FIFO
/// (First-In, First-Out) cost-basis method.
///
/// Algorithm:
/// 1. Filter Buy transactions → build a lot queue ordered by <c>TransactionDate</c> ascending.
/// 2. Filter Sell transactions (ordered by date) → consume lots from the front of the queue.
/// 3. Remaining entries in the queue form the open position.
/// </summary>
public static class FifoCostBasisCalculator
{
    /// <summary>
    /// Calculates the FIFO open position from the supplied transaction list.
    /// Only <c>Buy</c> and <c>Sell</c> transaction types are considered; all others are ignored.
    /// </summary>
    /// <param name="transactions">All transactions for a single asset (mixed types allowed).</param>
    /// <returns>A <see cref="FifoCostBasisResult"/> describing the remaining open lots.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the sell quantity exceeds the total available lot quantity (oversell).
    /// </exception>
    public static FifoCostBasisResult Calculate(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var ordered = transactions
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        // ── Step 1: Build the lot queue from Buy transactions ──────────────
        // Each entry is a mutable tuple so we can reduce quantity in place.
        var lots = new Queue<(DateOnly Date, decimal Quantity, decimal Price)>(
            ordered
                .Where(t => t.TransactionType == TransactionType.Buy)
                .Select(t => (t.TransactionDate, t.Quantity, t.PricePerUnit))
        );

        // ── Step 2: Consume lots for each Sell transaction ─────────────────
        foreach (var sell in ordered.Where(t => t.TransactionType == TransactionType.Sell))
        {
            var remaining = sell.Quantity;

            while (remaining > 0)
            {
                if (!lots.TryPeek(out var lot))
                    throw new InvalidOperationException(
                        $"Sell quantity exceeds available lots. " +
                        $"Oversell of {remaining} units on {sell.TransactionDate}.");

                if (lot.Quantity <= remaining)
                {
                    // Consume the whole lot
                    remaining -= lot.Quantity;
                    lots.Dequeue();
                }
                else
                {
                    // Partially consume the front lot
                    lots.Dequeue();
                    lots = new Queue<(DateOnly, decimal, decimal)>(
                        new[] { (lot.Date, lot.Quantity - remaining, lot.Price) }
                            .Concat(lots)
                    );
                    remaining = 0;
                }
            }
        }

        // ── Step 3: Build result from remaining lots ───────────────────────
        var openLots = lots
            .Select(l => new FifoCostBasisLot
            {
                PurchaseDate = l.Date,
                Quantity = l.Quantity,
                PricePerUnit = l.Price,
            })
            .ToList();

        var totalQty = openLots.Sum(l => l.Quantity);
        var totalCost = openLots.Sum(l => l.TotalCost);

        return new FifoCostBasisResult
        {
            TotalQuantity = totalQty,
            TotalCostBasis = totalCost,
            OpenLots = openLots,
        };
    }
}
