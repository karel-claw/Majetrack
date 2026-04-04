using FluentAssertions;
using Majetrack.Domain.CostBasis;
using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.Tests.CostBasis;

/// <summary>
/// Unit tests for FifoCostBasisCalculator.
/// Covers TC910, TC911, TC912.
/// </summary>
public class FifoCostBasisCalculatorTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Transaction MakeBuy(DateOnly date, decimal qty, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        AssetId = AssetId,
        TransactionType = TransactionType.Buy,
        TransactionDate = date,
        Quantity = qty,
        PricePerUnit = price,
        TotalAmount = qty * price,
        Currency = Currency.USD,
        Platform = Platform.Xtb,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Transaction MakeSell(DateOnly date, decimal qty, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        AssetId = AssetId,
        TransactionType = TransactionType.Sell,
        TransactionDate = date,
        Quantity = qty,
        PricePerUnit = price,
        TotalAmount = qty * price,
        Currency = Currency.USD,
        Platform = Platform.Xtb,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── TC910: SingleBuy_SingleSell_ReturnsPosition ───────────────────────

    [Fact]
    public void TC910_SingleBuy_SingleSell_ReturnsPosition()
    {
        // Arrange – buy 10 @ $100, sell all 10
        var transactions = new[]
        {
            MakeBuy(new DateOnly(2024, 1, 10), 10m, 100m),
            MakeSell(new DateOnly(2024, 2, 1), 10m, 150m),
        };

        // Act
        var result = FifoCostBasisCalculator.Calculate(transactions);

        // Assert – position fully closed
        result.TotalQuantity.Should().Be(0m);
        result.TotalCostBasis.Should().Be(0m);
        result.OpenLots.Should().BeEmpty();
    }

    // ── TC911: MultipleBuys_CorrectFifoOrder ─────────────────────────────

    [Fact]
    public void TC911_MultipleBuys_CorrectFifoOrder()
    {
        // Arrange – two buys, sell only the first lot
        var buy1 = MakeBuy(new DateOnly(2024, 1, 1), 5m, 100m);   // oldest → consumed first
        var buy2 = MakeBuy(new DateOnly(2024, 1, 15), 5m, 200m);  // newer → stays open
        var sell = MakeSell(new DateOnly(2024, 2, 1), 5m, 150m);

        var transactions = new[] { buy1, buy2, sell };

        // Act
        var result = FifoCostBasisCalculator.Calculate(transactions);

        // Assert – only the second lot remains
        result.TotalQuantity.Should().Be(5m);
        result.TotalCostBasis.Should().Be(1000m);   // 5 × $200
        result.AverageCostPerUnit.Should().Be(200m);
        result.OpenLots.Should().HaveCount(1);
        result.OpenLots[0].PurchaseDate.Should().Be(new DateOnly(2024, 1, 15));
        result.OpenLots[0].Quantity.Should().Be(5m);
        result.OpenLots[0].PricePerUnit.Should().Be(200m);
    }

    // ── TC912: PartialSell_RemainingLots ─────────────────────────────────

    [Fact]
    public void TC912_PartialSell_RemainingLots()
    {
        // Arrange – buy 10, sell only 3
        var buy = MakeBuy(new DateOnly(2024, 1, 1), 10m, 100m);
        var sell = MakeSell(new DateOnly(2024, 3, 1), 3m, 120m);

        var transactions = new[] { buy, sell };

        // Act
        var result = FifoCostBasisCalculator.Calculate(transactions);

        // Assert – 7 units remain from the original lot
        result.TotalQuantity.Should().Be(7m);
        result.TotalCostBasis.Should().Be(700m);    // 7 × $100
        result.AverageCostPerUnit.Should().Be(100m);
        result.OpenLots.Should().HaveCount(1);
        result.OpenLots[0].Quantity.Should().Be(7m);
        result.OpenLots[0].PricePerUnit.Should().Be(100m);
    }
}
