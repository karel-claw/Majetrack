using FluentAssertions;
using Majetrack.Domain.Enums;
using Majetrack.Infrastructure.CsvImport;

namespace Majetrack.Infrastructure.Tests.CsvImport;

/// <summary>
/// Tests for InvestownCsvImportParser accessed via CsvImportParserRegistry.
/// Covers TC801–TC802, TC810.
/// </summary>
public class InvestownCsvImportParserTests
{
    // ── Headers ───────────────────────────────────────────────────────────────

    private static readonly string[] InvestownHeaders =
    [
        "Datum", "Typ transakce", "Název projektu", "Typ projektu", "Částka", "Měna"
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
        return registry.GetParser(Platform.Investown)!;
    }

    /// <summary>
    /// Returns a sample Investown semicolon-delimited data row.
    /// Columns: Datum;Typ transakce;Název projektu;Typ projektu;Částka;Měna
    /// </summary>
    private static string BuildDataRow(
        string datum = "15.03.2025",
        string typTransakce = "Investice",
        string nazevProjektu = "Bytový dům Praha 5",
        string typProjektu = "Crowdfunding",
        string castka = "5000,00",
        string mena = "CZK")
        => $"{datum};{typTransakce};{nazevProjektu};{typProjektu};{castka};{mena}";

    // ── TC801: Parser_CanParse_WithInvestownHeaders_ReturnsTrue ──────────────

    [Fact]
    public void Parser_CanParse_WithInvestownHeaders_ReturnsTrue()
    {
        // Arrange
        var parser = GetParser();

        // Act
        var result = parser.CanParse(InvestownHeaders);

        // Assert
        result.Should().BeTrue();
    }

    // ── TC802: Parser_Parse_ReturnsRows ──────────────────────────────────────

    [Fact]
    public async Task Parser_Parse_ReturnsRows()
    {
        // Arrange
        var parser = GetParser();
        var headerLine = string.Join(";", InvestownHeaders);
        var csv = headerLine + "\n" + BuildDataRow();

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Should().NotBeNull();
        row.TransactionDate.Should().Be(new DateOnly(2025, 3, 15));
        row.TransactionType.Should().Be("Investice");
        row.Symbol.Should().Be("Bytový dům Praha 5");
        row.Currency.Should().Be("CZK");
        row.Price.Should().Be(5000.00m);
    }

    // ── TC810: Parser_CanParse_WithMismatchedHeaders_ReturnsFalse ────────────

    [Fact]
    public void Parser_CanParse_WithMismatchedHeaders_ReturnsFalse()
    {
        // Arrange
        var parser = GetParser();
        var wrongHeaders = new[] { "Date", "Type", "Amount", "Currency" };

        // Act
        var result = parser.CanParse(wrongHeaders);

        // Assert
        result.Should().BeFalse();
    }
}
