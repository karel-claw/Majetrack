using System.Globalization;
using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Parses CSV account-statement exports from the eToro platform.
/// eToro exports a transaction history with columns identified by name (not index).
/// Key columns: Date, Type, Details, Amount, Units, Realized Equity Change,
/// Realized Equity, Balance, Position ID, Asset type, NWA.
///
/// The "Type" column contains values like "Buy Stocks - AAPL/USD" which are
/// parsed into <see cref="CsvImportRow.TransactionType"/> ("Buy") and
/// <see cref="CsvImportRow.Symbol"/> ("AAPL/USD").
/// </summary>
internal sealed class EtoroCsvImportParser : ICsvImportParser
{
    // Column name constants (case-insensitive lookup)
    private const string ColDate = "Date";
    private const string ColType = "Type";
    private const string ColAmount = "Amount";
    private const string ColUnits = "Units";
    private const string ColRealizedEquityChange = "Realized Equity Change";
    private const string ColPositionId = "Position ID";

    private static readonly string[] DateFormats =
    [
        "MM/dd/yyyy HH:mm:ss",
        "MM/dd/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy"
    ];

    /// <inheritdoc/>
    public Platform Platform => Platform.Etoro;

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredHeaders { get; } =
    [
        "Date", "Type", "Details", "Amount", "Units",
        "Realized Equity Change", "Realized Equity", "Balance",
        "Position ID", "Asset type", "NWA"
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

        // Read and parse header row to get column indices by name
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
            return [];

        var columnIndex = BuildColumnIndex(headerLine);

        var rows = new List<CsvImportRow>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var row = ParseLine(line, columnIndex);
            if (row is not null)
                rows.Add(row);
        }

        return rows;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a case-insensitive dictionary mapping column name → zero-based index
    /// from the CSV header line.
    /// </summary>
    private static Dictionary<string, int> BuildColumnIndex(string headerLine)
    {
        var headers = headerLine.Split(',');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Length; i++)
        {
            var name = headers[i].Trim().Trim('"');
            if (!string.IsNullOrEmpty(name))
                index.TryAdd(name, i);
        }

        return index;
    }

    private static CsvImportRow? ParseLine(string line, Dictionary<string, int> columnIndex)
    {
        var parts = line.Split(',');

        // Date is required
        var rawDate = GetField(parts, columnIndex, ColDate);
        if (!TryParseDate(rawDate, out var transactionDate))
            return null;

        // Parse "Type" column: "Buy Stocks - AAPL/USD" → type="Buy", symbol="AAPL/USD"
        var rawType = GetField(parts, columnIndex, ColType);
        ParseActionColumn(rawType, out var transactionType, out var symbol);

        var externalId = GetField(parts, columnIndex, ColPositionId);
        var amount = TryParseDecimal(GetField(parts, columnIndex, ColAmount));
        var units = TryParseDecimal(GetField(parts, columnIndex, ColUnits));
        var profit = TryParseDecimal(GetField(parts, columnIndex, ColRealizedEquityChange));

        return new CsvImportRow
        {
            ExternalId = NullIfEmpty(externalId),
            TransactionType = transactionType,
            TransactionDate = transactionDate,
            Symbol = symbol,
            Profit = profit,
            Volume = units,
            Price = units.HasValue && units.Value != 0 && amount.HasValue
                ? Math.Round(amount.Value / units.Value, 6)
                : null,
            Commission = null,      // eToro does not export a separate commission column
            Swap = null,
            Currency = null,        // eToro exports do not include an explicit currency column
            ClosedDate = null
        };
    }

    /// <summary>
    /// Parses the eToro action column.
    /// Format: "&lt;Action&gt; &lt;AssetClass&gt; - &lt;Symbol&gt;"  e.g. "Buy Stocks - AAPL/USD"
    /// </summary>
    /// <param name="raw">Raw value from the Type column.</param>
    /// <param name="transactionType">The action part, e.g. "Buy" or "Sell".</param>
    /// <param name="symbol">The instrument symbol, e.g. "AAPL/USD".</param>
    private static void ParseActionColumn(
        string? raw,
        out string? transactionType,
        out string? symbol)
    {
        transactionType = null;
        symbol = null;

        if (string.IsNullOrWhiteSpace(raw))
            return;

        // Split on " - " separator to get action+class and symbol
        var dashIndex = raw.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            // Symbol is everything after " - "
            symbol = NullIfEmpty(raw[(dashIndex + 3)..].Trim());

            // Transaction type is the first word of the action part
            var actionPart = raw[..dashIndex].Trim();
            var spaceIndex = actionPart.IndexOf(' ');
            transactionType = spaceIndex >= 0
                ? NullIfEmpty(actionPart[..spaceIndex])
                : NullIfEmpty(actionPart);
        }
        else
        {
            // No dash separator — use the whole value as transaction type
            transactionType = NullIfEmpty(raw.Trim());
        }
    }

    private static string? GetField(string[] parts, Dictionary<string, int> columnIndex, string columnName)
    {
        if (!columnIndex.TryGetValue(columnName, out var idx) || idx >= parts.Length)
            return null;

        return parts[idx].Trim().Trim('"');
    }

    private static bool TryParseDate(string? raw, out DateOnly result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

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

        // Last resort: locale-aware parse
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dtLocale))
        {
            result = DateOnly.FromDateTime(dtLocale);
            return true;
        }

        return false;
    }

    private static decimal? TryParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim().Trim('"');
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? NullIfEmpty(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim().Trim('"');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
