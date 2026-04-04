# T-31: Unit Tests — Validators

## Current Validators

Only one validator exists in the codebase:

### `CreateTransactionValidator`

**Location:** `Majetrack.Features/Transactions/Create/CreateTransactionValidator.cs`  
**Base class:** `AbstractValidator<CreateTransactionRequest>` (FluentValidation)  
**Existing tests:** None (no test file found).

## Validator Rules Summary

| Field | Rules |
|---|---|
| `TransactionType` | Required; must parse to `TransactionType` enum (case-sensitive) |
| `TransactionDate` | Required; must be valid `DateOnly` (YYYY-MM-DD) |
| `TotalAmount` | Required; > 0 |
| `Currency` | Required; must parse to `Currency` enum (CZK, EUR, USD) |
| `Platform` | Required; must parse to `Platform` enum (Xtb, Etoro, Investown, XtbTradingHistory) |
| `Fee` | Optional; when provided, must be ≥ 0 |
| `Note` | Optional; when provided, max 1000 chars |
| **Conditional (Buy/Sell/Interest/Dividend):** | |
| `AssetId` | Required for asset types |
| `Quantity` | Required for asset types; > 0 |
| `PricePerUnit` | Required for asset types; > 0 |

## Test Coverage Plan

### 1. Required field validation (null/empty → error)

Each required field should have a test for `null` and `""` (where string):

- `TransactionType` null → error
- `TransactionType` empty → error
- `TransactionDate` null → error
- `TransactionDate` empty → error
- `TotalAmount` null → error
- `Currency` null → error
- `Currency` empty → error
- `Platform` null → error
- `Platform` empty → error

### 2. Enum parsing (invalid values → error)

- `TransactionType` = `"Invalid"` → error
- `TransactionType` = `"buy"` (lowercase) → error (case-sensitive)
- `Currency` = `"GBP"` → error
- `Currency` = `"czk"` (lowercase) → error
- `Platform` = `"Binance"` → error
- `Platform` = `"xtb"` (lowercase) → error

### 3. Enum parsing (valid values → pass)

- `TransactionType` each of: Buy, Sell, Deposit, Withdrawal, Interest, Dividend
- `Currency` each of: CZK, EUR, USD
- `Platform` each of: Xtb, Etoro, Investown, XtbTradingHistory

### 4. Numeric constraints

- `TotalAmount` = 0 → error
- `TotalAmount` = -1 → error
- `TotalAmount` = 0.01 → pass
- `Fee` = -1 → error
- `Fee` = 0 → pass
- `Fee` = null → pass (optional)

### 5. Date validation

- `TransactionDate` = `"2026-04-04"` → pass
- `TransactionDate` = `"not-a-date"` → error
- `TransactionDate` = `"04/04/2026"` → depends on `DateOnly.TryParse` behavior
- `TransactionDate` = `"2026-13-01"` → error (invalid month)

### 6. Conditional fields (asset-required types: Buy, Sell, Interest, Dividend)

For each asset-required type:
- `AssetId` null → error
- `Quantity` null → error
- `Quantity` = 0 → error
- `Quantity` = -1 → error
- `PricePerUnit` null → error
- `PricePerUnit` = 0 → error
- `PricePerUnit` = -1 → error

### 7. Conditional fields (non-asset types: Deposit, Withdrawal)

For Deposit and Withdrawal:
- `AssetId` null → pass (not required)
- `Quantity` null → pass
- `PricePerUnit` null → pass

### 8. Note validation

- `Note` = null → pass
- `Note` = 1000 chars → pass
- `Note` = 1001 chars → error

### 9. Happy path (full valid requests)

- Valid Buy request (all fields) → no errors
- Valid Deposit request (no asset fields) → no errors

## Test Structure

**File:** `Majetrack.Features.Tests/Transactions/Create/CreateTransactionValidatorTests.cs`

**Approach:**
- Use `[Theory]` + `[InlineData]` for parameterized enum/value tests
- Use a helper method to build a valid base request, then mutate one field per test
- Group tests by category (required fields, enums, conditionals, edge cases)
- No mocking needed — `AbstractValidator` is self-contained

## Estimated Test Count

~40–50 test cases covering all rules and edge conditions.
