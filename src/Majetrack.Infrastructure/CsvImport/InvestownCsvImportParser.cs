using System.Globalization;
using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Parses CSV transaction exports from the Investown platform (investown.cz).
/// Investown is a Czech P2P lending platform that exports transaction history via email.
///
/// CSV characteristics:
/// - Czech language headers and transaction type values
/// - Semicolon (;) delimiter (Czech Excel default)
/// - Comma decimal separator with optional space thousands separator (e.g. "5 000,00")
/// - All amounts are in CZK (Czech crowns)
/// - Required columns: Datum, Typ transakce, Název projektu, Částka
/// - Optional: Typ projektu, Měna, ID transakce, Poplatek, Poznámka
/// </summary>
internal sealed class InvestownCsvImportParser : ICsvImportParser
{
    // Column name constants
    private const string ColDatum = "Datum";
    private const string ColTypTransakce = "Typ transakce";
    private const string ColNazevProjektu = "Název projektu";
    private const string ColTypProjektu = "Typ projektu";
    private const string ColCastka = "Částka";
    private const string ColMena = "Měna";
    private const string ColIdTransakce = "ID transakce";
    private const string ColPoplatek = "Poplatek";
    private const string ColPoznamka = "Poznámka";

    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy",
        "dd.MM.yyyy HH:mm",
        "dd.MM.yyyy HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm:ss"
    ];

    /// <inheritdoc/>
    public Platform Platform => Platform.Investown;

    /// <inheritdoc/>
    /// <remarks>
    /// The minimal required set. Investown may add/remove columns across app versions;
    /// matching only these four ensures forward compatibility.
    /// </remarks>
    public IReadOnlyList<string> RequiredHeaders { get; } =
    [
        "Datum", "Typ transakce", "Název projektu", "Částka"
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

        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
            return [];

        // Auto-detect delimiter: Czech exports typically use semicolon
        var delimiter = headerLine.Contains(';') ? ';' : ',';

        var columnIndex = BuildColumnIndex(headerLine, delimiter);

        var rows = new List<CsvImportRow>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var row = ParseLine(line, delimiter, columnIndex);
            if (row is not null)
                rows.Add(row);
        }

        return rows;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static Dictionary<string, int> BuildColumnIndex(string headerLine, char delimiter)
    {
        var headers = headerLine.Split(delimiter);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < headers.Length; i++)
        {
            var name = headers[i].Trim().Trim('"');
            if (!string.IsNullOrEmpty(name))
                index.TryAdd(name, i);
        }

        return index;
    }

    private static CsvImportRow? ParseLine(string line, char delimiter, Dictionary<string, int> columnIndex)
    {
        var parts = SplitLine(line, delimiter);

        // Date is required
        var rawDate = GetField(parts, columnIndex, ColDatum);
        if (!TryParseDate(rawDate, out var transactionDate))
            return null;

        var transactionType = GetField(parts, columnIndex, ColTypTransakce);
        var nazevProjektu = GetField(parts, columnIndex, ColNazevProjektu);
        var typProjektu = GetField(parts, columnIndex, ColTypProjektu);
        var castka = TryParseDecimalCzech(GetField(parts, columnIndex, ColCastka));
        var mena = NullIfEmpty(GetField(parts, columnIndex, ColMena)) ?? "CZK";
        var externalId = NullIfEmpty(GetField(parts, columnIndex, ColIdTransakce));
        var poplatek = TryParseDecimalCzech(GetField(parts, columnIndex, ColPoplatek));
        var poznamka = NullIfEmpty(GetField(parts, columnIndex, ColPoznamka));

        // Build comment: project type + optional free-text note
        var comment = BuildComment(typProjektu, poznamka);

        return new CsvImportRow
        {
            ExternalId = externalId,
            TransactionType = NullIfEmpty(transactionType),
            TransactionDate = transactionDate,
            Symbol = NullIfEmpty(nazevProjektu),
            Comment = comment,
            Profit = null,          // P2P exports don't report per-row P&L
            Volume = null,          // Not applicable for P2P lending (no units/shares)
            Price = castka,         // Total transaction amount (Částka)
            Commission = poplatek.HasValue ? Math.Abs(poplatek.Value) : null,
            Swap = null,            // Not applicable for P2P lending
            Currency = mena,
            ClosedDate = null       // Investown CSV has no close date column
        };
    }

    private static string[] SplitLine(string line, char delimiter)
    {
        // Simple split; handles quoted fields containing the delimiter
        var parts = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == delimiter && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        parts.Add(current.ToString());
        return [.. parts];
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

    /// <summary>
    /// Parses a Czech-formatted decimal value.
    /// Handles comma decimal separator and optional space thousands separator.
    /// Examples: "5000,00" → 5000.00 | "5 000,00" → 5000.00 | "5000.00" → 5000.00
    /// </summary>
    private static decimal? TryParseDecimalCzech(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim().Trim('"');

        // Remove space thousands separators
        raw = raw.Replace(" ", "");

        // If value has comma but no dot → Czech decimal format (5000,00)
        if (raw.Contains(',') && !raw.Contains('.'))
            raw = raw.Replace(',', '.');

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? BuildComment(string? typProjektu, string? poznamka)
    {
        var typ = NullIfEmpty(typProjektu);
        var poz = NullIfEmpty(poznamka);

        if (typ is null && poz is null) return null;
        if (typ is null) return poz;
        if (poz is null) return typ;
        return $"{typ}; {poz}";
    }

    private static string? NullIfEmpty(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim().Trim('"');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
