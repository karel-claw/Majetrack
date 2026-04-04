using Majetrack.Domain.Enums;

namespace Majetrack.Infrastructure.CsvImport;

/// <summary>
/// Factory and registry for <see cref="ICsvImportParser"/> implementations.
/// Provides two lookup strategies:
/// <list type="bullet">
///   <item><description>Direct lookup by <see cref="Platform"/> enum value.</description></item>
///   <item>
///     <description>
///       Auto-detection by CSV headers: iterates registered parsers and returns the first
///       one whose <see cref="ICsvImportParser.CanParse"/> returns <see langword="true"/>.
///     </description>
///   </item>
/// </list>
/// New platform parsers are registered in the constructor; no other changes are required.
/// </summary>
public sealed class CsvImportParserRegistry
{
    private readonly IReadOnlyDictionary<Platform, ICsvImportParser> _parsers;

    /// <summary>
    /// Initialises the registry with all known platform parsers.
    /// </summary>
    public CsvImportParserRegistry()
    {
        var parsers = new ICsvImportParser[]
        {
            new XtbCsvImportParser(),
            // Future parsers: new EtoroCsvImportParser(), new InvestownCsvImportParser()
        };

        _parsers = parsers.ToDictionary(p => p.Platform);
    }

    /// <summary>
    /// Returns the parser registered for the specified <paramref name="platform"/>.
    /// </summary>
    /// <param name="platform">The brokerage platform to look up.</param>
    /// <returns>
    /// The matching <see cref="ICsvImportParser"/>, or <see langword="null"/> if no parser
    /// is registered for the given platform.
    /// </returns>
    public ICsvImportParser? GetParser(Platform platform) =>
        _parsers.TryGetValue(platform, out var parser) ? parser : null;

    /// <summary>
    /// Attempts to identify the correct parser by inspecting the CSV column headers.
    /// Returns the first registered parser whose <see cref="ICsvImportParser.CanParse"/>
    /// matches all supplied headers.
    /// </summary>
    /// <param name="headers">
    /// The column names extracted from the first row of an unknown CSV file.
    /// </param>
    /// <returns>
    /// The matching <see cref="ICsvImportParser"/>, or <see langword="null"/> if no registered
    /// parser recognises the header set.
    /// </returns>
    public ICsvImportParser? AutoDetect(IEnumerable<string> headers)
    {
        var headerList = headers.ToList();  // materialise once to avoid multiple enumeration
        return _parsers.Values.FirstOrDefault(p => p.CanParse(headerList));
    }
}
