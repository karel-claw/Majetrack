# Test Plan: T-11 — GET /api/assets

_Designer: test-scenario-designer | Date: 2026-04-03 | Task: T-11_

---

## Scope

Integration tests for `GET /api/assets` — a read endpoint that returns the shared asset catalogue
with optional `platform` and `assetType` query-string filters.

All tests are **integration tests** using `WebApplicationFactory<Program>` + Testcontainers PostgreSQL.
No unit tests — the handler contains only a conditional LINQ chain with no isolated business logic.

Auth is not yet implemented (T-07 / T-08). The endpoint is `AllowAnonymous` for the T-11 scope.
The future-auth test is included as `[Skip]` to serve as a handoff contract.

---

## Seed Data

All tests in the class share a single PostgreSQL container and a fixed, immutable seed.
Seed is inserted in `CreateHost` after `db.Database.EnsureCreated()`.

| # | Name                              | Platform   | AssetType | Currency | Ticker   | Exchange |
|---|-----------------------------------|------------|-----------|----------|----------|----------|
| 1 | Apple Inc.                        | Xtb        | Stock     | USD      | AAPL     | NASDAQ   |
| 2 | iShares Core MSCI EM ETF          | Etoro      | Etf       | USD      | IEMG     | NYSE     |
| 3 | Investown Loan Alpha              | Investown  | P2pLoan   | CZK      | null     | null     |
| 4 | Tesla Inc.                        | Etoro      | Stock     | USD      | TSLA     | NASDAQ   |
| 5 | Vanguard FTSE All-World UCITS ETF | Xtb        | Etf       | EUR      | VWCE.DE  | XETRA    |

Derived filter counts (used in assertions):

| Filter                              | Count | Names                                                        |
|-------------------------------------|-------|--------------------------------------------------------------|
| No filter                           | 5     | Apple Inc., iShares…, Investown Loan Alpha, Tesla Inc., Vanguard… |
| platform=Xtb                        | 2     | Apple Inc., Vanguard FTSE All-World UCITS ETF                |
| platform=Etoro                      | 2     | iShares Core MSCI EM ETF, Tesla Inc.                         |
| platform=Investown                  | 1     | Investown Loan Alpha                                         |
| assetType=Stock                     | 2     | Apple Inc., Tesla Inc.                                       |
| assetType=Etf                       | 2     | iShares Core MSCI EM ETF, Vanguard FTSE All-World UCITS ETF  |
| assetType=P2pLoan                   | 1     | Investown Loan Alpha                                         |
| platform=Xtb & assetType=Stock      | 1     | Apple Inc.                                                   |
| platform=Etoro & assetType=Etf      | 1     | iShares Core MSCI EM ETF                                     |
| platform=Investown & assetType=P2pLoan | 1  | Investown Loan Alpha                                         |
| platform=Xtb & assetType=P2pLoan   | 0     | (no Xtb P2P loans seeded)                                    |

---

## Test Fixture Design

```
GetAssetsTests
  implements IClassFixture<GetAssetsTests.TestFactory>, IAsyncLifetime
```

### TestFactory

`WebApplicationFactory<Program>` subclass that:
- Sets environment to `"Testing"` via `builder.UseEnvironment("Testing")`
- Replaces the default DbContext with `UseNpgsql(container.GetConnectionString())` + `UseSnakeCaseNamingConvention()`
- In `CreateHost`: calls `db.Database.EnsureCreated()` then seeds the 5 assets above
- No capturing logger needed (no log assertions in this test class)

Seed helper:

```csharp
// In TestFactory.CreateHost, after EnsureCreated:
db.Assets.AddRange(
    new Asset { Id = ..., Name = "Apple Inc.",                        Platform = Platform.Xtb,       AssetType = AssetType.Stock,   Currency = Currency.USD, Ticker = "AAPL",    Exchange = "NASDAQ", CreatedAt = DateTimeOffset.UtcNow },
    new Asset { Id = ..., Name = "iShares Core MSCI EM ETF",         Platform = Platform.Etoro,     AssetType = AssetType.Etf,     Currency = Currency.USD, Ticker = "IEMG",    Exchange = "NYSE",   CreatedAt = DateTimeOffset.UtcNow },
    new Asset { Id = ..., Name = "Investown Loan Alpha",              Platform = Platform.Investown, AssetType = AssetType.P2pLoan, Currency = Currency.CZK, Ticker = null,      Exchange = null,     CreatedAt = DateTimeOffset.UtcNow },
    new Asset { Id = ..., Name = "Tesla Inc.",                        Platform = Platform.Etoro,     AssetType = AssetType.Stock,   Currency = Currency.USD, Ticker = "TSLA",    Exchange = "NASDAQ", CreatedAt = DateTimeOffset.UtcNow },
    new Asset { Id = ..., Name = "Vanguard FTSE All-World UCITS ETF", Platform = Platform.Xtb,       AssetType = AssetType.Etf,     Currency = Currency.EUR, Ticker = "VWCE.DE", Exchange = "XETRA", CreatedAt = DateTimeOffset.UtcNow }
);
await db.SaveChangesAsync();
```

Use `Guid.NewGuid()` for IDs. Store them as fields on `TestFactory` so individual tests can assert exact `id` values when needed.

---

## Test Cases

---

### Group 1 — Happy Path (no filters)

#### TC-01: Returns HTTP 200

**Arrange:** Seed in place, `GET /api/assets` (no query params)
**Act:** Send request
**Assert:** Status code == `200 OK`

---

#### TC-02: Content-Type is application/json

**Arrange:** Same request as TC-01
**Act:** Send request
**Assert:** `Content-Type` header starts with `"application/json"`

---

#### TC-03: Returns all 5 seeded assets

**Arrange:** Same request as TC-01
**Act:** Deserialize response body as `List<AssetResponse>`
**Assert:** Count == `5`

---

#### TC-04: Each item contains all required fields with correct types

**Arrange:** Deserialize response
**Act:** Inspect the first item in the array
**Assert:**
- `id` is a non-empty `Guid` (parseable, not `Guid.Empty`)
- `name` is a non-null, non-empty string
- `assetType` is a non-null string
- `currency` is a non-null string
- `platform` is a non-null string

_Rationale: Validates the full response shape including field names (camelCase), not just count._

---

#### TC-05: Enum fields are serialized as PascalCase strings, not integers

**Arrange:** Deserialize response, find the Apple Inc. entry
**Act:** Inspect its field values
**Assert:**
- `assetType` == `"Stock"` (not `1`)
- `currency` == `"USD"` (not `3`)
- `platform` == `"Xtb"` (not `1`)

_Rationale: Guards against accidental integer enum serialization, which would break the public API contract._

---

#### TC-06: Results are ordered alphabetically by name (ASC)

**Arrange:** Deserialize response
**Act:** Extract the `name` field from each item in order
**Assert:** Names sequence ==
```
["Apple Inc.", "iShares Core MSCI EM ETF", "Investown Loan Alpha", "Tesla Inc.", "Vanguard FTSE All-World UCITS ETF"]
```

_Rationale: Ordering is a specified behavior (`ORDER BY name ASC`). Without this test, a future change to the EF query could silently break client sort assumptions._

---

#### TC-07: Nullable fields (Ticker, Exchange) are null for P2P loan

**Arrange:** Deserialize response, locate item where `name == "Investown Loan Alpha"`
**Act:** Inspect `ticker` and `exchange`
**Assert:**
- `ticker` is `null`
- `exchange` is `null`

_Rationale: P2P loans have no exchange ticker or listing venue. Null propagation through the LINQ Select must be explicitly verified._

---

#### TC-08: Non-null fields (Ticker, Exchange) are present for exchange-listed assets

**Arrange:** Deserialize response, locate item where `name == "Apple Inc."`
**Act:** Inspect `ticker` and `exchange`
**Assert:**
- `ticker` == `"AAPL"`
- `exchange` == `"NASDAQ"`

---

### Group 2 — Filter by Platform

#### TC-09: platform=Xtb returns only Xtb assets

**Arrange:** Seed in place
**Act:** `GET /api/assets?platform=Xtb`
**Assert:**
- Status == `200`
- Count == `2`
- All items have `platform == "Xtb"`
- Names == `["Apple Inc.", "Vanguard FTSE All-World UCITS ETF"]` (alphabetical)

---

#### TC-10: platform=Etoro returns only Etoro assets

**Act:** `GET /api/assets?platform=Etoro`
**Assert:**
- Count == `2`
- All items have `platform == "Etoro"`
- Names == `["iShares Core MSCI EM ETF", "Tesla Inc."]`

---

#### TC-11: platform=Investown returns only Investown assets

**Act:** `GET /api/assets?platform=Investown`
**Assert:**
- Count == `1`
- Item `platform` == `"Investown"`, `name` == `"Investown Loan Alpha"`

---

#### TC-12: platform filter is case-insensitive (platform=xtb)

**Act:** `GET /api/assets?platform=xtb`
**Assert:**
- Status == `200`
- Count == `2` (same result as TC-09 with `platform=Xtb`)
- All items have `platform == "Xtb"`

_Rationale: ASP.NET Core's default enum model binding is case-insensitive. Explicit verification prevents regressions if binding is ever customised._

---

#### TC-13: platform accepts integer value (platform=1 → Xtb)

**Act:** `GET /api/assets?platform=1`
**Assert:**
- Status == `200`
- Count == `2`
- All items have `platform == "Xtb"`

_Rationale: ASP.NET Core enum binding accepts numeric string representations. Documented in design doc Open Question #1; test pins the actual behavior so it is observable if it ever changes._

---

### Group 3 — Filter by AssetType

#### TC-14: assetType=Stock returns only stocks

**Act:** `GET /api/assets?assetType=Stock`
**Assert:**
- Status == `200`
- Count == `2`
- All items have `assetType == "Stock"`
- Names == `["Apple Inc.", "Tesla Inc."]`

---

#### TC-15: assetType=Etf returns only ETFs

**Act:** `GET /api/assets?assetType=Etf`
**Assert:**
- Count == `2`
- All items have `assetType == "Etf"`
- Names == `["iShares Core MSCI EM ETF", "Vanguard FTSE All-World UCITS ETF"]`

---

#### TC-16: assetType=P2pLoan returns only P2P loans

**Act:** `GET /api/assets?assetType=P2pLoan`
**Assert:**
- Count == `1`
- Item `assetType` == `"P2pLoan"`, `name` == `"Investown Loan Alpha"`

---

### Group 4 — Combined Filters

#### TC-17: platform=Xtb & assetType=Stock returns 1 asset

**Act:** `GET /api/assets?platform=Xtb&assetType=Stock`
**Assert:**
- Status == `200`
- Count == `1`
- Item: `name == "Apple Inc."`, `platform == "Xtb"`, `assetType == "Stock"`

---

#### TC-18: platform=Etoro & assetType=Etf returns 1 asset

**Act:** `GET /api/assets?platform=Etoro&assetType=Etf`
**Assert:**
- Count == `1`
- Item: `name == "iShares Core MSCI EM ETF"`, `platform == "Etoro"`, `assetType == "Etf"`

---

#### TC-19: platform=Investown & assetType=P2pLoan returns 1 asset

**Act:** `GET /api/assets?platform=Investown&assetType=P2pLoan`
**Assert:**
- Count == `1`
- Item: `name == "Investown Loan Alpha"`, `ticker == null`, `exchange == null`

---

#### TC-20: platform=Xtb & assetType=P2pLoan returns empty array (no match)

**Act:** `GET /api/assets?platform=Xtb&assetType=P2pLoan`
**Assert:**
- Status == `200` (NOT `404`)
- Count == `0`

_Rationale: This is the key contract for the "no results" case. An absent filter match is not an error — it is a valid empty catalogue response. 404 would be incorrect here._

---

### Group 5 — Empty / No-Match Results

#### TC-21: Response body for empty result is exactly an empty JSON array

**Arrange:** Same request as TC-20 (or any guaranteed zero-match filter)
**Act:** Read response body as raw string
**Assert:**
- Status == `200`
- Content-Type starts with `"application/json"`
- Body deserializes to an empty array (count == 0)

_Rationale: Separately verifies that `Results.Ok(emptyList)` serializes as `[]` and not `null`, `{}`, or omitted._

---

### Group 6 — Invalid Filter Values

#### TC-22: platform=InvalidString returns 400

**Act:** `GET /api/assets?platform=NotAPlatform`
**Assert:**
- Status == `400 Bad Request`

_Rationale: ASP.NET Core model binding cannot map "NotAPlatform" to a `Platform?` enum value. The framework returns a 400 before the handler is invoked — no custom validation is required, but this test pins that the framework guard is in place._

---

#### TC-23: assetType=InvalidString returns 400

**Act:** `GET /api/assets?assetType=ThisIsNotAnAssetType`
**Assert:**
- Status == `400 Bad Request`

---

#### TC-24: platform=999 (out-of-range integer) returns 200 with empty array

**Act:** `GET /api/assets?platform=999`
**Assert:**
- Status == `200`
- Count == `0`

_Rationale: ASP.NET Core binds integer strings to enums without range validation. `(Platform)999` is a syntactically valid C# enum value; binding succeeds. The EF WHERE clause produces zero matches. This behavior is different from an invalid string — it is intentional and documented here so the implementation team is not surprised._

---

### Group 7 — Future Auth (Placeholder)

#### TC-25: [Skip] Unauthenticated request returns 401 when auth is wired

**Skip reason:** `"Blocked by T-07: JWT bearer auth not yet configured. Remove [Skip] after T-07 lands and RequireAuthorization() is added to the AssetsFeature route group."`

**Future arrange:** Remove `AllowAnonymous` / add `RequireAuthorization()` to route group
**Future act:** `GET /api/assets` with no `Authorization` header
**Future assert:** Status == `401 Unauthorized`

---

## Out of Scope

| Scenario | Reason |
|----------|--------|
| Filtering by `currency` | Not in T-11 acceptance criteria |
| Pagination / cursor-based results | Not in T-11 scope; asset catalogue is bounded |
| `createdAt` field in response | Excluded from DTO per design doc Open Question #2 |
| DB failure (connection error) | Covered by `GlobalExceptionHandler` (T-09) — returns 500 ProblemDetails |
| User-scoped filtering | Assets are a shared catalogue; no user context applied |
| Performance / load | Out of scope |
| Unit tests for handler | Handler has no isolated logic; integration tests give better signal |

---

## Implementation Notes for Test Author

1. **Shared factory:** Use `IClassFixture<TestFactory>` — one PostgreSQL container for the entire class.
   Individual tests are read-only against the fixed seed; no per-test cleanup is needed.

2. **Stable IDs:** Generate deterministic `Guid` values in `TestFactory` (e.g., `Guid.Parse("...")`)
   and expose them as `public Guid AppleId`, `public Guid InvestownLoanId`, etc.
   This allows `TC-04` and nullable-field tests to assert exact `id` values without string-matching names.

3. **Deserialization helper:**
   ```csharp
   private async Task<List<AssetResponse>> DeserializeAsync(HttpResponseMessage response)
   {
       var json = await response.Content.ReadAsStringAsync();
       return JsonSerializer.Deserialize<List<AssetResponse>>(json,
           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
   }
   ```
   Define `AssetResponse` as a local record in the test file (or import from the feature project if accessible).

4. **Enum string assertions:** Assert on the string-serialized value (e.g., `"Stock"`, `"Xtb"`, `"USD"`),
   not on parsed enum members. This guards against accidental `JsonConverter` misconfiguration.

5. **TC-12 (case-insensitive):** ASP.NET Core's `EnumConverter` is case-insensitive by default.
   If the team ever wraps enum binding in a custom converter, this test will catch the regression.

6. **TC-24 (out-of-range int):** This test documents a known ASP.NET Core quirk — do not add
   FluentValidation to "fix" it unless product explicitly requires rejecting undefined enum ints.

7. **TC-25 (Skip):** Use `[Fact(Skip = "...")]` with a descriptive reason message. Do not delete
   this test — it is the handoff contract for T-07.

---

_Total test cases: 25 (8 happy-path shape, 5 platform filter, 3 assetType filter, 4 combined, 2 empty-result, 3 invalid-input, 1 auth placeholder)_
