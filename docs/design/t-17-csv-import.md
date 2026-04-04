# Design: T-17 CSV Import Infrastructure

**Status:** Design only — no code yet  
**Depends on:** T-12 (Transaction entity, Platform/TransactionType/Currency enums)  
**Author:** Karel (architect subagent)

---

## 1. Summary

Platform-agnostic CSV import infrastructure. Each supported platform (XTB, eToro, Investown) gets its own parser that knows how to read that platform's export format and map rows into a common `CsvImportRow` model. A registry provides lookup by `Platform` enum and auto-detection from CSV headers.

The parsers produce `CsvImportRow` objects — they do **not** persist anything. Persistence is the responsibility of a higher-level import service (future ticket) that validates, deduplicates via `ExternalId`, resolves assets, and calls the transaction repository.

---

## 2. Feature Location

```
src/Majetrack.Features/Import/Csv/
```

---

## 3. Files to Create

| File | Layer | Purpose |
|------|-------|---------|
| `ICsvImportParser.cs` | Features/Import/Csv | Parser interface — platform identity, header detection, stream parsing |
| `CsvImportRow.cs` | Features/Import/Csv | Flat row model — common fields every parser produces |
| `CsvImportParserRegistry.cs` | Features/Import/Csv | Registry — lookup by Platform, auto-detect from headers |
| `ICsvImportParserRegistry.cs` | Features/Import/Csv | Registry interface for DI |
| `CsvImportFeature.cs` | Features/Import/Csv | DI registration module (registers registry + all parsers) |

No files to modify. Parsers for specific platforms (XTB, eToro, Investown) are separate tickets.

---

## 4. ICsvImportParser Interface

```
ICsvImportParser
├── Platform Platform { get; }
│   Which platform this parser handles (enum from Domain)
│
├── bool CanParse(string[] headers)
│   Given the first-row headers of a CSV, returns true if this parser
│   recognizes the format. Used for auto-detection when platform is unknown.
│
└── List<CsvImportRow> Parse(Stream csv, int skipHeaderLines = 1)
    Reads the stream, skips N header lines, returns parsed rows.
    Throws on malformed data (caller handles).
```

### Design decisions

- **`Stream` input, not `string`** — caller controls file I/O; parsers don't care whether it's upload, disk, or blob.
- **`skipHeaderLines` default 1** — most broker CSVs have a single header row. Some (eToro) have metadata rows above — parser can override default.
- **Returns `List<T>`, not `IEnumerable`** — import is a batch operation; full materialization is expected and enables count/validation before persist.
- **No async** — CSV parsing is CPU-bound in-memory work. Stream is already loaded by the caller. If a parser ever needs async (unlikely), the interface can gain a `ParseAsync` overload later.

---

## 5. CsvImportRow Model

Flat record with all fields needed to create a `Transaction` entity. Parsers map platform-specific columns into these common fields.

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| `TransactionDate` | `DateOnly` | ✅ | Parsed from platform's date format |
| `TransactionType` | `TransactionType` | ✅ | Mapped from platform-specific strings (e.g. "Market Buy" → Buy) |
| `AssetTicker` | `string?` | ❌ | Ticker/symbol as the platform names it; null for Deposit/Withdrawal |
| `AssetName` | `string?` | ❌ | Human-readable name if the CSV provides it |
| `Quantity` | `decimal` | ✅ | Number of units; 1 for cash transactions |
| `PricePerUnit` | `decimal` | ✅ | Price in original currency; equals TotalAmount for cash txns |
| `TotalAmount` | `decimal` | ✅ | Gross amount of the transaction |
| `Currency` | `Currency` | ✅ | Mapped to domain enum |
| `Platform` | `Platform` | ✅ | Set by the parser (matches `ICsvImportParser.Platform`) |
| `Fee` | `decimal` | ✅ | 0 if platform doesn't report fees |
| `Note` | `string?` | ❌ | Optional; parser can populate from a "description" column |
| `ExternalId` | `string?` | ❌ | Platform-specific row ID for deduplication (e.g. XTB transaction ID) |
| `RawLine` | `int` | ✅ | 1-based line number in source CSV — useful for error reporting |

### Why `AssetTicker` not `AssetId`?

Parsers don't know our internal asset IDs. They output ticker strings. The import service (future ticket) resolves tickers to `Asset` entities, creating new ones if needed.

---

## 6. CsvImportParserRegistry

### Interface

```
ICsvImportParserRegistry
├── ICsvImportParser? GetParser(Platform platform)
│   Direct lookup — returns null if no parser registered for that platform.
│
└── ICsvImportParser? DetectParser(string[] headers)
    Iterates registered parsers, returns first where CanParse(headers) == true.
    Returns null if no parser matches.
```

### Implementation

- Constructor receives `IEnumerable<ICsvImportParser>` (all registered parsers via DI).
- Builds an internal `Dictionary<Platform, ICsvImportParser>` on construction for O(1) lookup.
- `DetectParser` iterates the list and calls `CanParse` on each. Order doesn't matter — header sets should be mutually exclusive across platforms.
- Throws `InvalidOperationException` if two parsers register for the same `Platform` (fail-fast on misconfiguration).

### DI Registration (CsvImportFeature.cs)

```
services.AddSingleton<ICsvImportParserRegistry, CsvImportParserRegistry>();

// Future per-platform parsers register themselves:
// services.AddSingleton<ICsvImportParser, XtbCsvParser>();
// services.AddSingleton<ICsvImportParser, EtoroCsvParser>();
// services.AddSingleton<ICsvImportParser, InvestownCsvParser>();
```

Parsers are registered as `ICsvImportParser` — the registry collects all of them. Adding a new platform = add one class + one DI line. No registry code changes.

---

## 7. Acceptance Criteria Mapping

| Criterion | Covered by |
|-----------|-----------|
| Interface defined | `ICsvImportParser.cs` — Platform, CanParse, Parse |
| Row model defined | `CsvImportRow.cs` — all common fields + RawLine for diagnostics |
| Registry with platform lookup | `CsvImportParserRegistry.cs` — GetParser + DetectParser |

---

## 8. Open Questions

1. **Should `Parse` throw or return `ErrorOr<List<CsvImportRow>>`?** — Current design: throw on malformed CSV (consistent with parser being a low-level component). The import service wraps this in try/catch and returns user-friendly errors. Can revisit if we want structured parse errors per row.

2. **ISIN vs ticker?** — Some platforms export ISIN instead of ticker. `AssetTicker` field should accept either; the resolution logic (future ticket) handles both.

3. **Encoding?** — Most broker exports are UTF-8. If a platform uses Windows-1250 (possible for Czech brokers), the parser should handle encoding internally.

---

_Date: 2026-04-04_
