using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Defines the contract for a platform-specific CSV transaction parser.
/// Each implementation handles the distinct export format of a single brokerage platform
/// (e.g. XTB, eToro, Investown) and converts raw CSV data into <see cref="CsvImportRow"/> instances.
/// </summary>
public interface ICsvImportParser
{
    /// <summary>
    /// The brokerage platform this parser handles.
    /// Used by <see cref="CsvImportParserRegistry"/> to locate the correct parser.
    /// </summary>
    Platform Platform { get; }

    /// <summary>
    /// The ordered set of CSV column headers that this parser expects in the first row.
    /// Used by <see cref="CanParse"/> and <see cref="CsvImportParserRegistry.AutoDetect"/> to
    /// identify whether a given CSV file matches this parser's format.
    /// </summary>
    IReadOnlyList<string> RequiredHeaders { get; }

    /// <summary>
    /// Determines whether the supplied CSV headers match this parser's expected format.
    /// </summary>
    /// <param name="headers">
    /// The column names extracted from the first row of a CSV file, in order.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if every required header is present (case-insensitive);
    /// otherwise <see langword="false"/>.
    /// </returns>
    bool CanParse(IEnumerable<string> headers);

    /// <summary>
    /// Parses the CSV content from the provided stream and returns a collection of
    /// normalised import rows.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the beginning of the CSV file.
    /// The caller is responsible for disposing the stream.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation requests.</param>
    /// <returns>
    /// A list of <see cref="CsvImportRow"/> objects, one per non-header data row.
    /// Rows that cannot be parsed are skipped.
    /// </returns>
    Task<IReadOnlyList<CsvImportRow>> ParseAsync(Stream stream, CancellationToken cancellationToken = default);
}
