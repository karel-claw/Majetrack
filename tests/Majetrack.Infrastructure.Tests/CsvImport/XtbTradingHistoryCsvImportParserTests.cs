using FluentAssertions;
using Majetrack.Domain.Enums;
using Majetrack.Infrastructure.CsvImport;

namespace Majetrack.Infrastructure.Tests.CsvImport;

/// <summary>
/// Tests for XtbTradingHistoryCsvImportParser accessed via CsvImportParserRegistry.
/// Covers TC601–TC603.
/// </summary>
public class XtbTradingHistoryCsvImportParserTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] XtbTradingHistoryHeaders =
    [
        "OpenTime", "Type", "Symbol", "Volume", "Profit",
        "Commission", "Swap", "OpenPrice", "ClosePrice",
        "StopLoss", "TakeProfit", "Magic", "Comment"
    ];

    private static Stream ToCsvStream(string csv)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return new MemoryStream(bytes);
    }

    private static ICsvImportParser GetParser()
    {
        var registry = new CsvImportParserRegistry();
        return registry.GetParser(Platform.XtbTradingHistory)!;
    }

    private static string BuildDataRow() =>
        // OpenTime, Type, Symbol, Volume, Profit, Commission, Swap, OpenPrice, ClosePrice, StopLoss, TakeProfit, Magic, Comment
        "2024.03.15 09:30:00,BUY,EURUSD,0.10,125.50,-2.00,-0.50,1.08500,1.09750,1.08000,1.10000,0,Test trade";

    // ── TC601: Parser_CanParse_WithXtbHeaders_ReturnsTrue ────────────────────

    [Fact]
    public void Parser_CanParse_WithXtbHeaders_ReturnsTrue()
    {
        // Arrange
        var parser = GetParser();

        // Act
        var result = parser.CanParse(XtbTradingHistoryHeaders);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Parser_CanParse_WithMismatchedHeaders_ReturnsFalse()
    {
        // Arrange
        var parser = GetParser();
        var wrongHeaders = new[] { "ID", "Type", "Time", "Symbol", "Profit" };

        // Act
        var result = parser.CanParse(wrongHeaders);

        // Assert
        result.Should().BeFalse();
    }

    // ── TC602: Parser_Parse_ReturnsRows ──────────────────────────────────────

    [Fact]
    public async Task Parser_Parse_ReturnsRows()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", XtbTradingHistoryHeaders);
        var csv = headerLine + "\n" + BuildDataRow();

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);

        var row = rows[0];
        row.TransactionDate.Should().Be(new DateOnly(2024, 3, 15));
        row.TransactionType.Should().Be("BUY");
        row.Symbol.Should().Be("EURUSD");
        row.Volume.Should().Be(0.10m);
        row.Profit.Should().Be(125.50m);
        row.Commission.Should().Be(-2.00m);
        row.Swap.Should().Be(-0.50m);
        row.Price.Should().Be(1.08500m);   // OpenPrice → Price
        row.Comment.Should().Be("Test trade");
    }

    [Fact]
    public async Task Parser_Parse_EmptyStream_ReturnsEmpty()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", XtbTradingHistoryHeaders);
        var csv = headerLine; // no data rows

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Parser_Parse_MultipleRows_ReturnsAll()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", XtbTradingHistoryHeaders);
        var csv = headerLine + "\n" + BuildDataRow() + "\n" + BuildDataRow();

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(2);
    }

    // ── TC603: Currency_DerivedFromSymbol ────────────────────────────────────

    [Fact]
    public async Task Currency_DerivedFromSymbol_ForexPair_ReturnsQuoteCurrency()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", XtbTradingHistoryHeaders);
        // EURUSD → quote currency is USD
        var dataRow = "2024.03.15 09:30:00,BUY,EURUSD,0.10,125.50,-2.00,-0.50,1.08500,1.09750,1.08000,1.10000,0,";
        var csv = headerLine + "\n" + dataRow;

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        // EURUSD format: base=EUR, quote=USD — currency is derived from the 6-char forex symbol
        rows[0].Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Currency_DerivedFromSymbol_StockSymbol_ReturnsNull()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", XtbTradingHistoryHeaders);
        // AAPL.US → not a 6-char all-letter symbol, currency cannot be derived
        var dataRow = "2024.03.15 09:30:00,BUY,AAPL.US,10,500.00,-2.50,-0.00,180.00,185.00,175.00,190.00,0,";
        var csv = headerLine + "\n" + dataRow;

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        // Cannot derive currency from non-forex symbol — null is acceptable
        rows[0].Currency.Should().BeNull();
    }
}
