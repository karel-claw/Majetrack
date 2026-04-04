using FluentAssertions;
using Majetrack.Domain.Enums;
using Majetrack.Infrastructure.CsvImport;

namespace Majetrack.Infrastructure.Tests.CsvImport;

/// <summary>
/// Tests for ICsvImportParser implementations and CsvImportParserRegistry.
/// Covers TC501–TC504, TC510, TC511.
/// </summary>
public class CsvImportParserTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a minimal in-memory stream from a CSV string.</summary>
    private static Stream ToCsvStream(string csv)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return new MemoryStream(bytes);
    }

    // ── TC501: Registry_GetParserByPlatform_ReturnsParser ────────────────────

    [Fact]
    public void Registry_GetParserByPlatform_ReturnsParser()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();

        // Act – request a known platform
        var parser = registry.GetParser(Platform.Xtb);

        // Assert
        parser.Should().NotBeNull();
        parser!.Platform.Should().Be(Platform.Xtb);
    }

    // ── TC502: Parser_CanParse_WithMatchingHeaders_ReturnsTrue ───────────────

    [Fact]
    public void Parser_CanParse_WithMatchingHeaders_ReturnsTrue()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();
        var parser = registry.GetParser(Platform.Xtb)!;
        var headers = parser.RequiredHeaders;   // use the parser's own canonical headers

        // Act
        var result = parser.CanParse(headers);

        // Assert
        result.Should().BeTrue();
    }

    // ── TC503: Parser_Parse_ReturnsRows ─────────────────────────────────────

    [Fact]
    public async Task Parser_Parse_ReturnsRows()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();
        var parser = registry.GetParser(Platform.Xtb)!;

        // Build a CSV with one header row and one data row using the parser's headers
        var headerLine = string.Join(",", parser.RequiredHeaders);
        var dataLine = BuildXtbDataRow();
        var csv = headerLine + "\n" + dataLine;

        await using var stream = ToCsvStream(csv);

        // Act
        var rows = await parser.ParseAsync(stream, CancellationToken.None);

        // Assert
        rows.Should().HaveCountGreaterThanOrEqualTo(1);
        var row = rows.First();
        row.Should().NotBeNull();
        // Spot-check a few mapped fields
        row.TransactionDate.Should().NotBe(default);
    }

    // ── TC504: Registry_AutoDetect_ByHeaders_ReturnsParser ──────────────────

    [Fact]
    public void Registry_AutoDetect_ByHeaders_ReturnsParser()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();
        // Grab the XTB parser's required headers
        var xtbHeaders = registry.GetParser(Platform.Xtb)!.RequiredHeaders;

        // Act
        var detected = registry.AutoDetect(xtbHeaders);

        // Assert
        detected.Should().NotBeNull();
        detected!.Platform.Should().Be(Platform.Xtb);
    }

    // ── TC510: Registry_GetUnknownPlatform_ReturnsNull ───────────────────────

    [Fact]
    public void Registry_GetUnknownPlatform_ReturnsNull()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();

        // Act – cast to an undefined enum value
        var parser = registry.GetParser((Platform)999);

        // Assert
        parser.Should().BeNull();
    }

    // ── TC511: Parser_CanParse_WithMismatchedHeaders_ReturnsFalse ───────────

    [Fact]
    public void Parser_CanParse_WithMismatchedHeaders_ReturnsFalse()
    {
        // Arrange
        var registry = new CsvImportParserRegistry();
        var parser = registry.GetParser(Platform.Xtb)!;
        var wrongHeaders = new[] { "WrongHeader1", "WrongHeader2", "WrongHeader3" };

        // Act
        var result = parser.CanParse(wrongHeaders);

        // Assert
        result.Should().BeFalse();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Returns a sample XTB CSV data row matching the XTB parser headers.</summary>
    private static string BuildXtbDataRow()
    {
        // Values correspond to XtbCsvImportParser.RequiredHeaders order:
        // ID, Type, Time, Symbol, Comment, Profit, Volume, Price, Commission, Swap, Currency, Closed
        return "12345,BUY,2024-01-15,AAPL,Buy Apple,500.00,10,50.00,-2.50,0.00,USD,2024-01-15";
    }
}
