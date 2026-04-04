# Design: T-21 POST /api/transactions/import — ImportCsvEndpoint

**Status:** Design only — no code yet  
**Depends on:** T-12 (Transaction entity), T-17 (CSV infrastructure), T-18/T-19/T-20 (platform parsers)  
**Author:** Karel (architect subagent)

---

## 1. Summary

HTTP endpoint that accepts a CSV file upload, parses it using the platform parsers from T-17–T-20, validates and maps rows to `Transaction` entities, deduplicates by `ExternalId`, and persists them. Returns 201 with the count of imported transactions.

This is the integration layer that ties together `CsvImportParserRegistry`, `ICsvImportParser`, and the existing `Transaction` entity + `MajetrackDbContext`.

---

## 2. API Contract

### POST /api/transactions/import

**Auth:** Bearer JWT (`ICurrentUser` provides `UserId`)  
**Content-Type:** `multipart/form-data`

#### Request

| Part | Type | Required | Description |
|------|------|----------|-------------|
| `file` | `IFormFile` | ✅ | The CSV file to import |
| `platform` | `string` | ❌ | Platform name (e.g. `"Xtb"`, `"Etoro"`, `"Investown"`). If omitted, auto-detect from headers. |

#### Response 201 Created

```json
{
  "imported": 42,
  "skipped": 3,
  "total": 45
}
```

- `imported` — transactions actually persisted (new rows)
- `skipped` — rows skipped due to deduplication (`ExternalId` already exists)
- `total` — total rows parsed from the CSV

#### Error Responses

| Status | Condition |
|--------|-----------|
| 400 | No file uploaded, empty file, unsupported format, or validation errors |
| 401 | Unauthenticated (no/invalid JWT) |
| 422 | CSV parsed but rows contain mapping errors (e.g. unknown currency, unknown transaction type) |

#### 400 Validation Error Shape

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "file": ["File is required."],
    "platform": ["'Foo' is not a valid platform."]
  }
}
```

#### 422 Row-Level Error Shape

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Import validation failed.",
  "status": 422,
  "errors": [
    { "row": 3, "field": "Currency", "message": "'GBP' is not a supported currency." },
    { "row": 7, "field": "TransactionType", "message": "Cannot map 'Futures' to a known transaction type." }
  ]
}
```

Row errors are returned as an array (not grouped by field) because the field context is per-row.

---

## 3. ErrorOr Chain

The endpoint follows the project's standard `ErrorOr` + `ToHttpResult` pattern from T-10.

```
1. Authenticate
   └─ ICurrentUser.UserId is null → Error.Unauthorized

2. Validate request (file + platform param)
   └─ FluentValidation: file required, file not empty, platform valid enum if provided
   └─ Fail → ErrorType.Validation → 400

3. Resolve parser
   ├─ platform provided → registry.GetParser(platform)
   └─ platform omitted → read header line, registry.AutoDetect(headers)
   └─ No parser found → Error.Validation("platform", "Unable to detect CSV format. Specify platform explicitly.") → 400

4. Parse CSV
   └─ parser.ParseAsync(file.OpenReadStream(), ct)
   └─ Throws on malformed CSV → catch → Error.Validation("file", "...") → 400

5. Map & Validate rows
   ├─ For each CsvImportRow:
   │   ├─ Map TransactionType string → TransactionType enum (fail → collect row error)
   │   ├─ Map Currency string → Currency enum (fail → collect row error)
   │   └─ Validate required fields (TransactionDate, etc.)
   └─ Any row errors → Error.Custom(422, ...) → 422

6. Resolve assets (batch)
   ├─ Collect distinct Symbol values from rows where Symbol != null
   ├─ Query db: Assets where Ticker IN (symbols) AND UserId = currentUser
   ├─ Build Dictionary<string, Guid> (ticker → AssetId)
   └─ Unknown tickers → create new Asset entities (auto-create on import)
       - AssetType inferred from Platform (Investown → P2pLoan, Xtb/Etoro → Stock)
       - Name = Symbol (placeholder, user can rename later)

7. Deduplicate
   ├─ Collect ExternalIds from rows where ExternalId != null
   ├─ Query db: Transactions where ExternalId IN (ids) AND UserId = currentUser AND Platform = platform
   ├─ Build HashSet<string> of existing ExternalIds
   └─ Filter out rows with matching ExternalId → count as "skipped"

8. Persist (batch)
   ├─ Map remaining CsvImportRow[] → Transaction[] with:
   │   ├─ Id = Guid.NewGuid()
   │   ├─ UserId = currentUser.UserId
   │   ├─ AssetId = resolved from step 6 (null for Deposit/Withdrawal)
   │   ├─ TransactionType, TransactionDate, Currency, Platform from mapping
   │   ├─ Quantity = Volume ?? 1
   │   ├─ PricePerUnit = Price ?? TotalAmount
   │   ├─ TotalAmount = derived (Volume * Price, or Price if no Volume)
   │   ├─ Fee = Commission ?? 0
   │   ├─ Note = Comment
   │   ├─ ExternalId = row.ExternalId
   │   └─ CreatedAt = DateTimeOffset.UtcNow
   ├─ db.Transactions.AddRange(transactions)
   └─ db.SaveChangesAsync(ct)

9. Return 201
   └─ Results.Created with { imported, skipped, total }
```

---

## 4. CsvImportRow → Transaction Mapping

The `CsvImportRow` model (T-17) uses different field names than `Transaction` (T-12). The mapping:

| CsvImportRow | Transaction | Notes |
|-------------|-------------|-------|
| `TransactionDate` | `TransactionDate` | Direct |
| `TransactionType` (string) | `TransactionType` (enum) | Requires mapping logic per platform |
| `Symbol` | `AssetId` (Guid) | Resolved via asset lookup/creation |
| `Volume` | `Quantity` | Default 1 for cash txns |
| `Price` | `PricePerUnit` | Default = TotalAmount for cash txns |
| `Commission` | `Fee` | `Math.Abs()`, default 0 |
| `Currency` (string) | `Currency` (enum) | Enum.TryParse |
| `Comment` | `Note` | Direct |
| `ExternalId` | `ExternalId` | Direct |
| `Profit` | *(not mapped)* | Informational; not stored on Transaction |
| `Swap` | *(not mapped)* | Informational; not stored on Transaction |
| `ClosedDate` | *(not mapped)* | Not stored on Transaction |

### TransactionType string → enum mapping

Each platform uses different raw strings. Centralize this in the feature:

| Raw string | TransactionType enum |
|-----------|---------------------|
| `"BUY"`, `"Market Buy"`, `"Investice"` | `Buy` |
| `"SELL"`, `"Market Sell"` | `Sell` |
| `"DEPOSIT"`, `"Vklad"` | `Deposit` |
| `"WITHDRAWAL"`, `"Výběr"` | `Withdrawal` |
| `"DIVIDEND"`, `"Dividend"`, `"Dividenda"` | `Dividend` |
| `"INTEREST"`, `"Výnos"`, `"Splátka jistiny"` | `Interest` |

Unknown types → row-level validation error.

### TotalAmount derivation

`CsvImportRow` doesn't have a `TotalAmount` field. Derive it:
- If `Volume` and `Price` both present: `TotalAmount = Volume * Price`
- If only `Price`: `TotalAmount = Price` (cash transactions)
- If only `Profit` (e.g. closed XTB positions): `TotalAmount = Math.Abs(Profit)`

---

## 5. Files to Create

| File | Layer | Purpose |
|------|-------|---------|
| `src/Majetrack.Features/Transactions/Import/ImportCsvEndpoint.cs` | Features | Minimal API endpoint (maps route, extracts form data, calls feature) |
| `src/Majetrack.Features/Transactions/Import/ImportCsvFeature.cs` | Features | Orchestrates the full ErrorOr chain (steps 1–9) |
| `src/Majetrack.Features/Transactions/Import/ImportCsvRequest.cs` | Features | Request DTO (`IFormFile File`, `string? Platform`) |
| `src/Majetrack.Features/Transactions/Import/ImportCsvResponse.cs` | Features | Response DTO (`int Imported`, `int Skipped`, `int Total`) |
| `src/Majetrack.Features/Transactions/Import/ImportCsvValidator.cs` | Features | FluentValidation: file required, not empty, platform valid if provided |
| `src/Majetrack.Features/Transactions/Import/ImportErrors.cs` | Features | Error factory methods for import-specific errors |
| `src/Majetrack.Features/Transactions/Import/CsvRowMapper.cs` | Features | Maps `CsvImportRow` → `Transaction`, handles type/currency/amount mapping |
| `tests/Majetrack.Features.Tests/Transactions/Import/ImportCsvFeatureTests.cs` | Tests | Unit tests for the feature |
| `tests/Majetrack.Features.Tests/Transactions/Import/CsvRowMapperTests.cs` | Tests | Unit tests for row mapping logic |

### Files to Modify

| File | Change |
|------|--------|
| `src/Majetrack.Features/DependencyInjection.cs` (or equivalent) | Register `ImportCsvFeature`, `ImportCsvValidator` |
| Endpoint registration (Program.cs or feature module) | Map `POST /api/transactions/import` |

---

## 6. ImportErrors

```
ImportErrors
├── Unauthenticated         → Error.Unauthorized — no valid JWT
├── FileRequired            → Error.Validation("file", "...") — no file in request
├── FileEmpty               → Error.Validation("file", "...") — 0 bytes
├── InvalidPlatform(name)   → Error.Validation("platform", "...") — unknown platform string
├── ParserNotFound          → Error.Validation("platform", "...") — auto-detect failed
├── ParseFailed(message)    → Error.Validation("file", "...") — malformed CSV
├── RowValidationFailed(errors) → Error.Custom(type: 422, ...) — row-level mapping errors
```

---

## 7. Validation Rules (ImportCsvValidator)

| Field | Rules |
|-------|-------|
| `File` | Required, `Length > 0`, extension `.csv` (optional, nice-to-have) |
| `Platform` | If provided: must be a valid `Platform` enum name (case-insensitive) |

File size limit: defer to ASP.NET defaults (28.6 MB) or configure in `Program.cs` if needed. Not a validator concern.

---

## 8. Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Auto-create assets on import | Yes | User uploads CSV to get started fast; requiring pre-created assets is terrible UX |
| 422 for row errors (not 400) | 422 Unprocessable Entity | Request itself is valid (file uploaded, platform correct) but content can't be processed; 422 is semantically precise |
| Batch asset resolution | Single `WHERE IN` query | N+1 for each ticker would be slow for large imports |
| Batch deduplication | Single `WHERE IN` query | Same — avoid N queries for ExternalId checks |
| Dedup scope: UserId + Platform + ExternalId | Composite | ExternalId alone could clash across platforms; same user could import from multiple brokers |
| Return skipped count | Yes | User needs to know re-import is safe and what was skipped |
| `CsvRowMapper` as separate class | Yes | Row mapping is complex enough to unit test independently |
| No async parser wrapping | Use `ParseAsync` as-is | Parsers already define `ParseAsync` on `ICsvImportParser` |

---

## 9. Open Questions

1. **File size limit** — Should we enforce a max file size (e.g. 5 MB)? Broker exports are typically small (< 1 MB) but better to be explicit.

2. **Partial success** — Current design is all-or-nothing: if any row fails validation, return 422 with all errors and persist nothing. Alternative: import valid rows, skip invalid, return both counts. All-or-nothing is safer for V1.

3. **Asset type inference** — Auto-created assets infer `AssetType` from `Platform` (Investown → P2pLoan, others → Stock). This is wrong for ETFs on XTB/eToro. Options: (a) default to Stock, let user edit later; (b) lookup against a known ticker database. Option (a) is simpler for V1.

4. **TransactionType mapping extensibility** — The raw-string-to-enum mapping is hardcoded. If platforms add new transaction types in their exports, this breaks. Consider a mapping configuration or fallback to `Interest` for unknown income-like types?

---

_Date: 2026-04-04_
