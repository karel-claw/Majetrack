using FluentAssertions;
using Majetrack.Domain.Enums;
using Majetrack.Infrastructure.CsvImport;

namespace Majetrack.Infrastructure.Tests.CsvImport;

/// <summary>
/// Tests for EtoroCsvImportParser accessed via CsvImportParserRegistry.
/// Covers TC701–TC703, TC710.
/// </summary>
public class EtoroCsvImportParserTests
{
    // ── Headers ───────────────────────────────────────────────────────────────

    private static readonly string[] EtoroHeaders =
    [
        "Date", "Type", "Details", "Amount", "Units",
        "Realized Equity Change", "Realized Equity", "Balance",
        "Position ID", "Asset type", "NWA"
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Stream ToCsvStream(string csv)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return new MemoryStream(bytes);
    }

    private static ICsvImportParser GetParser()
    {
        var registry = new CsvImportParserRegistry();
        return registry.GetParser(Platform.Etoro)!;
    }

    /// <summary>
    /// Returns a sample eToro data row.
    /// Columns: Date, Type, Details, Amount, Units, Realized Equity Change,
    ///          Realized Equity, Balance, Position ID, Asset type, NWA
    /// </summary>
    private static string BuildDataRow(
        string date = "01/15/2024 10:30:00",
        string type = "Buy Stocks - AAPL/USD",
        string details = "AAPL",
        string amount = "150.00",
        string units = "1",
        string realizedEquityChange = "0.00",
        string realizedEquity = "1000.00",
        string balance = "5000.00",
        string positionId = "123456789",
        string assetType = "Stocks",
        string nwa = "0")
        => $"{date},{type},{details},{amount},{units},{realizedEquityChange},{realizedEquity},{balance},{positionId},{assetType},{nwa}";

    // ── TC701: Parser_CanParse_WithEtoroHeaders_ReturnsTrue ───────────────────

    [Fact]
    public void Parser_CanParse_WithEtoroHeaders_ReturnsTrue()
    {
        // Arrange
        var parser = GetParser();

        // Act
        var result = parser.CanParse(EtoroHeaders);

        // Assert
        result.Should().BeTrue();
    }

    // ── TC702: Parser_Parse_ReturnsRows ──────────────────────────────────────

    [Fact]
    public async Task Parser_Parse_ReturnsRows()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", EtoroHeaders);
        var csv = headerLine + "\n" + BuildDataRow();

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Should().NotBeNull();
        row.TransactionDate.Should().NotBe(default);
        row.TransactionDate.Should().Be(new DateOnly(2024, 1, 15));
    }

    // ── TC703: ActionColumn_ParsedToTypeAndSymbol ─────────────────────────────

    [Fact]
    public async Task ActionColumn_ParsedToTypeAndSymbol()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", EtoroHeaders);
        // "Buy Stocks - AAPL/USD" → Type=Buy, Symbol=AAPL/USD
        var csv = headerLine + "\n" + BuildDataRow(type: "Buy Stocks - AAPL/USD");

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        var row = rows[0];
        row.TransactionType.Should().Be("Buy");
        row.Symbol.Should().Be("AAPL/USD");
    }

    // ── TC710: Parser_CanParse_WithMismatchedHeaders_ReturnsFalse ────────────

    [Fact]
    public void Parser_CanParse_WithMismatchedHeaders_ReturnsFalse()
    {
        // Arrange
        var parser = GetParser();
        var wrongHeaders = new[] { "ID", "Time", "Symbol", "Profit" };

        // Act
        var result = parser.CanParse(wrongHeaders);

        // Assert
        result.Should().BeFalse();
    }

    // ── Additional edge-case coverage ────────────────────────────────────────

    [Fact]
    public async Task ActionColumn_SellStocks_ParsedCorrectly()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", EtoroHeaders);
        var csv = headerLine + "\n" + BuildDataRow(type: "Sell Stocks - TSLA/USD");

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        rows[0].TransactionType.Should().Be("Sell");
        rows[0].Symbol.Should().Be("TSLA/USD");
    }

    [Fact]
    public async Task Parser_Parse_EmptyStream_ReturnsEmpty()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(",", EtoroHeaders);
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
        var headerLine = string.Join(",", EtoroHeaders);
        var csv = headerLine + "\n" + BuildDataRow() + "\n" + BuildDataRow(type: "Sell Stocks - TSLA/USD");

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(2);
    }
}
