using System.Globalization;
using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Parses CSV transaction exports from the XTB (X-Trade Brokers) trading history format.
/// XTB's trading history report uses fixed columns in the following order:
/// OpenTime, Type, Symbol, Volume, Profit, Commission, Swap, OpenPrice, ClosePrice,
/// StopLoss, TakeProfit, Magic, Comment.
/// Date format: YYYY.MM.DD HH:MI:SS
/// </summary>
internal sealed class XtbTradingHistoryCsvImportParser : ICsvImportParser
{
    // Column indices match RequiredHeaders order.
    private const int ColOpenTime    = 0;
    private const int ColType        = 1;
    private const int ColSymbol      = 2;
    private const int ColVolume      = 3;
    private const int ColProfit      = 4;
    private const int ColCommission  = 5;
    private const int ColSwap        = 6;
    private const int ColOpenPrice   = 7;
    private const int ColClosePrice  = 8;
    private const int ColStopLoss    = 9;
    private const int ColTakeProfit  = 10;
    private const int ColMagic       = 11;
    private const int ColComment     = 12;

    private static readonly string[] DateFormats =
    [
        "yyyy.MM.dd HH:mm:ss",
        "yyyy.MM.dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy"
    ];

    /// <inheritdoc/>
    public Platform Platform => Platform.XtbTradingHistory;

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredHeaders { get; } =
    [
        "OpenTime", "Type", "Symbol", "Volume", "Profit",
        "Commission", "Swap", "OpenPrice", "ClosePrice",
        "StopLoss", "TakeProfit", "Magic", "Comment"
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
        if (parts.Length < ColComment + 1)
            return null;

        // OpenTime is required — skip rows where it cannot be parsed
        if (!TryParseDate(parts[ColOpenTime], out var openDate))
            return null;

        var symbol = NullIfEmpty(parts[ColSymbol]);
        var currency = DeriveForexCurrency(symbol);

        return new CsvImportRow
        {
            TransactionDate = openDate,
            TransactionType = NullIfEmpty(parts[ColType]),
            Symbol          = symbol,
            Volume          = TryParseDecimal(parts[ColVolume]),
            Profit          = TryParseDecimal(parts[ColProfit]),
            Commission      = TryParseDecimal(parts[ColCommission]),
            Swap            = TryParseDecimal(parts[ColSwap]),
            Price           = TryParseDecimal(parts[ColOpenPrice]),  // OpenPrice → Price
            ClosedDate      = TryParseDateAsDateOnly(parts[ColClosePrice]) is { } cd ? cd : null,
            Comment         = NullIfEmpty(parts[ColComment]),
            Currency        = currency,
        };
    }

    /// <summary>
    /// Derives the quote currency from a 6-character forex pair symbol (e.g. EURUSD → USD).
    /// Returns <see langword="null"/> for non-forex symbols such as "AAPL.US".
    /// </summary>
    private static string? DeriveForexCurrency(string? symbol)
    {
        if (symbol is null || symbol.Length != 6)
            return null;

        // Quick sanity check: all characters must be ASCII letters
        if (!symbol.All(char.IsLetter))
            return null;

        return symbol.Substring(3, 3).ToUpperInvariant();
    }

    private static bool TryParseDate(string raw, out DateOnly result)
    {
        raw = raw.Trim().Trim('"');
        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;

            if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse a raw string as a <see cref="DateOnly"/>.
    /// Returns <see langword="null"/> when the value is empty or not a date
    /// (e.g. when the field contains a price value like "1.09750").
    /// </summary>
    private static DateOnly? TryParseDateAsDateOnly(string raw)
    {
        if (TryParseDate(raw, out var d))
            return d;
        return null;
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
