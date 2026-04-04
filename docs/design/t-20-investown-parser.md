# T-20: InvestownCsvImportParser — Design Document

## Summary

Implement an Investown CSV parser that reads the transaction export from the Investown P2P lending platform and maps rows into `CsvImportRow` instances via the `ICsvImportParser` interface.

**Key facts about Investown:**
- Czech P2P lending / crowdfunding platform (`investown.cz`)
- Users invest in consumer and business loans for interest income
- CSV export available via app: Můj profil → Dokumenty → "Zaslat seznam transakcí"
- Export is emailed to the user as a CSV attachment
- All amounts are in **CZK** (Czech crowns)
- CSV contains: transaction type, date, project name, project type (confirmed by Investown blog)

---

## Source: Investown Transaction CSV

### How Users Get the CSV

1. Log into Investown app or `my.investown.cz`
2. Go to Můj profil → Dokumenty
3. Click "Zaslat seznam transakcí"
4. CSV arrives via email within minutes

### Expected Headers

Based on Investown's documented export format (Czech-language CSV), the expected columns are:

| # | Header (CZ)          | Header (EN equivalent) | Example Value                   |
|---|----------------------|------------------------|---------------------------------|
| 0 | `Datum`              | Date                   | `15.03.2025` or `15.03.2025 10:30` |
| 1 | `Typ transakce`      | Transaction Type       | `Investice`, `Výnos`, `Vklad`, `Výběr` |
| 2 | `Název projektu`     | Project Name           | `Bytový dům Praha 5`           |
| 3 | `Typ projektu`       | Project Type           | `Crowdfunding`, `Participace`  |
| 4 | `Částka`             | Amount                 | `5000.00` or `5 000,00`        |
| 5 | `Měna`               | Currency               | `CZK`                          |

> **⚠️ IMPORTANT — Header names are assumed.** Investown does not publicly document the exact CSV column names. The headers above are inferred from the blog post description ("typ a datum transakce, název a typu projektu") and standard Czech fintech conventions. **Before implementation, Honza should export an actual Investown CSV and verify the exact headers.** The parser design accommodates header variations.

### Potential Additional Columns

These columns may or may not be present — handle gracefully if found:

| Header (CZ)           | Purpose                              |
|------------------------|--------------------------------------|
| `ID transakce`         | External transaction identifier      |
| `Stav`                 | Status (e.g. `Dokončeno`, `Čeká`)    |
| `Poznámka` / `Popis`  | Free-text note or description        |
| `Poplatek`             | Platform fee                         |
| `Úrok`                 | Interest amount (for yield rows)     |

---

## Transaction Types

Investown generates these transaction types in user accounts:

| Czech Value              | Meaning                        | CsvImportRow.TransactionType |
|--------------------------|--------------------------------|------------------------------|
| `Vklad`                  | Deposit to Investown wallet    | `"Vklad"` (raw passthrough)  |
| `Výběr`                  | Withdrawal from wallet         | `"Výběr"`                    |
| `Investice`              | Investment into a project      | `"Investice"`                |
| `Výnos`                  | Interest/yield payment         | `"Výnos"`                    |
| `Splátka jistiny`        | Principal repayment            | `"Splátka jistiny"`          |
| `Vrácení investice`      | Investment return (early exit) | `"Vrácení investice"`        |
| `Odměna` / `Bonus`       | Referral reward / bonus        | `"Odměna"` / `"Bonus"`      |
| `Poplatek`               | Platform fee charged           | `"Poplatek"`                 |
| `Smluvní pokuta`         | Contractual penalty (late)     | `"Smluvní pokuta"`           |
| `Úrok z prodlení`        | Late payment interest          | `"Úrok z prodlení"`          |

> Transaction types are passed through as raw Czech strings. Downstream mapping to `TransactionType` enum is the import service's responsibility (future ticket).

---

## Field Mapping to CsvImportRow

| CsvImportRow Property | Source Column             | Mapping Logic                                                              |
|-----------------------|---------------------------|----------------------------------------------------------------------------|
| `ExternalId`          | `ID transakce` (if present) | Direct mapping; `null` if column absent                                  |
| `TransactionType`     | `Typ transakce`           | Raw string passthrough (Czech values)                                      |
| `TransactionDate`     | `Datum`                   | Parse as `DateOnly`. See Date Parsing below.                               |
| `Symbol`              | `Název projektu`          | Project name as the "symbol" — e.g. `"Bytový dům Praha 5"`. This is the closest equivalent to a ticker for P2P loans. |
| `Comment`             | `Typ projektu` + `Poznámka` | Concatenate project type and optional note. E.g. `"Crowdfunding"` or `"Crowdfunding; volný text"` |
| `Profit`              | *(derived)*               | `null` — P2P platforms don't report per-row P&L in exports                 |
| `Volume`              | `null`                    | Not applicable for P2P lending (no units/shares)                           |
| `Price`               | `null`                    | Not applicable for P2P lending                                             |
| `Commission`          | `Poplatek` (if present)   | Decimal, `Math.Abs()`. `null` if column absent.                            |
| `Swap`                | `null`                    | Not applicable for P2P lending                                             |
| `Currency`            | `Měna` (if present)       | Direct mapping; fallback to `"CZK"` if column absent (Investown is CZK-only) |
| `ClosedDate`          | `null`                    | Not applicable — Investown CSV has no close date column                     |

### Why `Symbol` = Project Name?

Investown projects (loans) don't have tickers or ISINs. The project name (e.g. "Bytový dům Praha 5") serves as the asset identifier. The import service will resolve this to an `Asset` entity of type `P2pLoan`.

### Unmapped Source Columns

| Column               | Reason                                                        |
|----------------------|---------------------------------------------------------------|
| `Stav`               | Status info, not needed for transaction import                |
| `Typ projektu`       | Folded into `Comment` field                                   |

---

## Date Parsing

Investown is a Czech platform. Expected date formats:

| Format                   | Example                |
|--------------------------|------------------------|
| `dd.MM.yyyy`             | `15.03.2025`           |
| `dd.MM.yyyy HH:mm`      | `15.03.2025 10:30`     |
| `dd.MM.yyyy HH:mm:ss`   | `15.03.2025 10:30:00`  |
| `yyyy-MM-dd`             | `2025-03-15`           |
| `yyyy-MM-dd HH:mm:ss`   | `2025-03-15 10:30:00`  |

Parse with `DateTime.TryParseExact` using `CultureInfo.InvariantCulture`, then convert to `DateOnly`.

---

## Number Parsing

Czech CSV exports may use either format:

| Style              | Example    | Notes                                         |
|--------------------|------------|-----------------------------------------------|
| Dot decimal        | `5000.00`  | Machine-generated exports                     |
| Comma decimal      | `5 000,00` | Czech locale with thousands space separator   |

**Strategy:**
1. Strip whitespace (thousands separator).
2. If value contains `,` but no `.` → replace `,` with `.`.
3. Parse with `decimal.TryParse(..., InvariantCulture)`.

---

## CSV Delimiter

Czech CSV exports commonly use `;` (semicolon) as delimiter instead of `,` (comma). The parser should:

1. Read the header line.
2. If it contains `;` → use semicolon as delimiter.
3. Otherwise → use comma.

This auto-detection approach (same as checking the first line) is simple and reliable.

---

## CanParse() — Header Detection

### Required Headers (minimal set)

```
["Datum", "Typ transakce", "Název projektu", "Částka"]
```

These four columns are the core fields confirmed by Investown's documentation. `CanParse()` returns `true` if all four are present (case-insensitive).

### Why minimal?

Investown may add/remove columns between app versions. Matching only the essential columns ensures forward compatibility.

---

## Design Decisions

1. **Czech raw strings for TransactionType** — Don't translate to English. The import service handles mapping Czech transaction types to domain `TransactionType` enum. Keeps the parser simple and faithful to source data.

2. **Project name as Symbol** — Unconventional but correct for P2P. There's no ticker. The project name is the only stable identifier for the loan/investment.

3. **Default currency CZK** — Investown operates exclusively in Czech crowns. Even if the `Měna` column is absent, hardcode fallback to `"CZK"`.

4. **Semicolon delimiter support** — Czech Excel exports default to `;`. Auto-detect from header line.

5. **Encoding** — Expect UTF-8 (modern app exports). Handle BOM via `StreamReader` default. If issues arise with Czech characters (ěščřžýáíé), may need to try `Encoding.GetEncoding("windows-1250")` as fallback.

6. **Volume/Price/Profit = null** — P2P lending transactions are fundamentally different from stock trades. There are no units, no per-unit prices, no realized P&L per row. The `Částka` (amount) maps conceptually to the total transaction value, but since `CsvImportRow` doesn't have a direct `TotalAmount` field, we can't map it cleanly. **Open question:** Should `Částka` map to `Price` (as total amount) or stay unmapped? See Open Questions below.

---

## Edge Cases

| Edge Case                              | Handling                                                      |
|----------------------------------------|---------------------------------------------------------------|
| Empty file / header-only               | Return empty list                                             |
| Missing `Datum`                        | Skip row (required field)                                     |
| Czech diacritics in project names      | Preserve as-is; UTF-8 handles this                            |
| Semicolon delimiter                    | Auto-detect from header line                                  |
| Comma decimal separator (`5 000,00`)   | Strip spaces, replace `,` with `.`, parse                     |
| BOM (byte order mark)                  | `StreamReader` default handles UTF-8 BOM                      |
| Unknown transaction types              | Pass through as raw string                                    |
| Trailing empty lines                   | `string.IsNullOrWhiteSpace` → skip                            |
| Quoted fields with delimiter inside    | Handle with quote-aware parsing                               |
| `Stav` = `Čeká` (pending transaction) | Parse normally; downstream decides whether to import pending  |

---

## File Location

```
src/Majetrack.Infrastructure/CsvImport/InvestownCsvImportParser.cs
```

Follows the same pattern as `EtoroCsvImportParser.cs` and `XtbCsvImportParser.cs`.

---

## Open Questions

1. **Exact CSV headers** — The header names in this doc are educated guesses based on Investown's blog description and Czech fintech conventions. **Honza needs to export an actual CSV from his Investown account** and share the header row so we can confirm exact column names before implementation.

2. **Amount field mapping** — `Částka` is the transaction amount, but `CsvImportRow` doesn't have a `TotalAmount` property. Options:
   - Map to `Price` (treating it as total value with Volume=1) — hacky but functional
   - Map to `Profit` for yield rows — semantically wrong for investments/deposits
   - **Recommended:** Map to `Price` with `Volume = 1` for all rows. This makes `Price` effectively mean "total amount" for P2P transactions. The import service can interpret this correctly knowing `Platform = Investown`.

3. **Multiple transaction types per project** — A single Investown project generates multiple transactions over its lifetime (investment, monthly yields, principal repayment). Each is a separate CSV row. The import service needs to handle these as distinct transactions, not updates to one.

4. **Encoding confirmation** — Is the CSV UTF-8 or Windows-1250? Modern app exports should be UTF-8, but worth verifying with a real file.

---

## Implementation Checklist (for developer)

- [ ] Create `InvestownCsvImportParser` implementing `ICsvImportParser`
- [ ] `Platform => Platform.Investown`
- [ ] Define `RequiredHeaders` (minimal Czech set)
- [ ] Auto-detect delimiter (`;` vs `,`) from header line
- [ ] Implement `CanParse()` — case-insensitive, checks required headers subset
- [ ] Implement `ParseAsync()` — line-by-line, name-based column access
- [ ] Czech date parser (dd.MM.yyyy variants)
- [ ] Czech number parser (handle comma decimal + space thousands)
- [ ] Register in DI (`services.AddSingleton<ICsvImportParser, InvestownCsvImportParser>()`)
- [ ] Unit tests: happy path, semicolon delimiter, Czech numbers, diacritics, edge cases

---

_Date: 2026-04-04_
