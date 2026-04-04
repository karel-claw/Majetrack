using System.Globalization;
using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Parses CSV transaction exports from the XTB (X-Trade Brokers) platform.
/// XTB exports a closed-positions report with fixed columns in the following order:
/// ID, Type, Time, Symbol, Comment, Profit, Volume, Price, Commission, Swap, Currency, Closed.
/// </summary>
internal sealed class XtbCsvImportParser : ICsvImportParser
{
    // Column indices match RequiredHeaders order.
    private const int ColExternalId = 0;
    private const int ColType = 1;
    private const int ColTime = 2;
    private const int ColSymbol = 3;
    private const int ColComment = 4;
    private const int ColProfit = 5;
    private const int ColVolume = 6;
    private const int ColPrice = 7;
    private const int ColCommission = 8;
    private const int ColSwap = 9;
    private const int ColCurrency = 10;
    private const int ColClosed = 11;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss",
        "dd.MM.yyyy",
        "dd.MM.yyyy HH:mm:ss",
        "MM/dd/yyyy"
    ];

    /// <inheritdoc/>
    public Platform Platform => Platform.Xtb;

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredHeaders { get; } =
    [
        "ID", "Type", "Time", "Symbol", "Comment",
        "Profit", "Volume", "Price", "Commission", "Swap",
        "Currency", "Closed"
    ];

    /// <inheritdoc/>
    public bool CanParse(IEnumerable<string> headers)
    {
        var headerSet = new HashSet<string>(
            headers.Select(h => h.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return RequiredHeaders.All(required => headerSet.Contains(required));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CsvImportRow>> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);

        // Skip header row
        var header = await reader.ReadLineAsync(cancellationToken);
        if (header is null)
            return [];

        var rows = new List<CsvImportRow>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var row = ParseLine(line);
            if (row is not null)
                rows.Add(row);
        }

        return rows;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static CsvImportRow? ParseLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 12)
            return null;

        // TransactionDate is required — skip rows where it cannot be parsed
        if (!TryParseDate(parts[ColTime], out var transactionDate))
            return null;

        TryParseDate(parts[ColClosed], out var closedDate);

        return new CsvImportRow
        {
            ExternalId = NullIfEmpty(parts[ColExternalId]),
            TransactionType = NullIfEmpty(parts[ColType]),
            TransactionDate = transactionDate,
            Symbol = NullIfEmpty(parts[ColSymbol]),
            Comment = NullIfEmpty(parts[ColComment]),
            Profit = TryParseDecimal(parts[ColProfit]),
            Volume = TryParseDecimal(parts[ColVolume]),
            Price = TryParseDecimal(parts[ColPrice]),
            Commission = TryParseDecimal(parts[ColCommission]),
            Swap = TryParseDecimal(parts[ColSwap]),
            Currency = NullIfEmpty(parts[ColCurrency]),
            ClosedDate = closedDate == default ? null : closedDate
        };
    }

    private static bool TryParseDate(string raw, out DateOnly result)
    {
        raw = raw.Trim().Trim('"');
        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            // Also try parsing as DateTime (when time component is present)
            if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
        }

        result = default;
        return false;
    }

    private static decimal? TryParseDecimal(string raw)
    {
        raw = raw.Trim().Trim('"');
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? NullIfEmpty(string raw)
    {
        var trimmed = raw.Trim().Trim('"');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
