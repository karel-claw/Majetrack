# T-14 Design: Wire CNB FX Rate into CreateTransactionFeature

**Status:** Design only — no code yet  
**Depends on:** T-12 (CreateTransactionFeature pipeline), T-13 (IExchangeRateProvider / CnbExchangeRateProvider)  
**Author:** Karel (architect subagent)

---

## 1. Summary

T-14 adds **Step 4 (FX rate resolution)** to the `CreateTransactionFeature` pipeline, inserting it between the existing Step 3 (asset resolution) and the final persist step.

When a transaction is in a non-CZK currency, the feature must:
1. Call `IExchangeRateProvider` to get the rate for `{currency} → CZK` on the transaction date.
2. If the rate is unavailable (provider returns `null`) → return a `502 Bad Gateway` error.
3. If the rate is available → attach it to the persisted `Transaction` entity for downstream reporting.

CZK transactions skip this step entirely (rate = 1:1 by identity).

---

## 2. Files to Modify

### Core feature
| File | Change |
|---|---|
| `src/Majetrack.Features/Transactions/Create/CreateTransactionFeature.cs` | Inject `IExchangeRateProvider`; add FX step between asset resolution and persist |
| `src/Majetrack.Features/Transactions/Create/TransactionErrors.cs` | `FxRateUnavailable` already defined — no change needed |

### Domain entity
| File | Change |
|---|---|
| `src/Majetrack.Domain/Entities/Transaction.cs` | Add `decimal? FxRateToCzk` property |
| `src/Majetrack.Infrastructure/Persistence/Configurations/TransactionConfiguration.cs` | Map new `FxRateToCzk` column with precision (18, 8), nullable |

> **Note:** `IFxRateProvider` in `Majetrack.Features/Shared/Services/` already exists but has a different signature (takes `Currency` enum + `DateOnly`). Do NOT use that interface here — it lives in Features and would create an upward dependency from Features onto Infrastructure types. Instead, wire `IExchangeRateProvider` directly from Infrastructure. See §4 for DI strategy.

### Infrastructure registration
| File | Change |
|---|---|
| `src/Majetrack.Infrastructure/InfrastructureRegistration.cs` (or wherever `CnbExchangeRateProvider` is registered) | Ensure `IExchangeRateProvider` → `CnbExchangeRateProvider` is registered as `Scoped` or `Singleton` |

### Tests
| File | Change |
|---|---|
| `tests/Majetrack.Features.Tests/Transactions/CreateTransactionTests.cs` | Add TC050–TC053 FX scenarios (see §4); update `CreateFeature()` factory to accept optional `IExchangeRateProvider` mock |

---

## 3. ErrorOr Chain with FX Step

The pipeline in `CreateTransactionFeature.ExecuteAsync` after T-14:

```
Step 1: Authenticate        → 401 if no UserId
Step 2: Validate            → 400 ValidationProblem on failure
Step 3: Resolve asset       → 404 if asset missing or owned by another user
                              (skipped for Deposit/Withdrawal)
Step 4: Resolve FX rate     → skip if currency == CZK
                              → call IExchangeRateProvider.GetRateAsync(currency, "CZK", ct)
                              → if null → return TransactionErrors.FxRateUnavailable (502)
                              → else capture rate value
Step 5: Persist             → Transaction.FxRateToCzk = resolved rate (or null for CZK)
                              → SaveChangesAsync
Step 6: Return Guid
```

### FX step detail

```csharp
// Pseudocode — design only
decimal? fxRateToCzk = null;
if (currency != Currency.CZK)
{
    var rate = await _exchangeRateProvider.GetRateAsync(currency.ToString(), "CZK", ct);
    if (rate is null)
    {
        return TransactionErrors.FxRateUnavailable;
    }
    fxRateToCzk = rate;
}

// ... proceed to persist with fxRateToCzk attached
```

`IExchangeRateProvider.GetRateAsync(string from, string to, CancellationToken)` already handles the same-currency shortcut (returns `1m` when `from == to`), but we skip the call entirely for CZK as a clarity/performance optimization.

---

## 4. DI Strategy & Mocking in Tests

### DI wiring (production)

`IExchangeRateProvider` lives in `Majetrack.Infrastructure`. `CreateTransactionFeature` lives in `Majetrack.Features`. The Features project **already references Infrastructure** (it uses `MajetrackDbContext`), so injecting `IExchangeRateProvider` is a valid dependency — no new project reference needed.

Register in `Program.cs` (or the existing Infrastructure DI extension):
```csharp
// Already expected from T-13; confirm it's wired:
services.AddSingleton<IExchangeRateProvider, CnbExchangeRateProvider>();
```

Inject into `CreateTransactionFeature` constructor:
```csharp
public CreateTransactionFeature(
    MajetrackDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateTransactionRequest> validator,
    IExchangeRateProvider exchangeRateProvider)   // NEW
```

### Mock strategy in tests

Current `CreateTransactionTests` wires `CreateTransactionFeature` manually without DI:
```csharp
private CreateTransactionFeature CreateFeature()
    => new(_db, _currentUserMock.Object, _validator);
```

After T-14, update the factory to accept an optional mock:
```csharp
private Mock<IExchangeRateProvider> _fxProviderMock = new();

private CreateTransactionFeature CreateFeature()
{
    // Default: FX provider returns a rate for any currency
    _fxProviderMock
        .Setup(x => x.GetRateAsync(It.IsAny<string>(), "CZK", It.IsAny<CancellationToken>()))
        .ReturnsAsync(25.50m);

    return new(_db, _currentUserMock.Object, _validator, _fxProviderMock.Object);
}
```

Existing tests (TC001–TC008, TC040, TC043–TC044) must continue passing with the default mock returning a valid rate. Tests that exercise CZK transactions (TC003 Deposit, TC004 Withdrawal) will naturally skip the FX call — assert `_fxProviderMock.Verify(x => x.GetRateAsync(...), Times.Never)` optionally.

#### New test cases

| TC | Scenario | Setup | Expected |
|---|---|---|---|
| TC050 | USD Buy — FX rate resolved | mock returns `25.50m` | success, `Transaction.FxRateToCzk == 25.50m` |
| TC051 | EUR Dividend — FX rate resolved | mock returns `24.80m` | success, `Transaction.FxRateToCzk == 24.80m` |
| TC052 | CZK Deposit — FX skipped | mock not called | success, `Transaction.FxRateToCzk == null` |
| TC053 | USD Buy — FX provider unavailable | mock returns `null` | `ErrorType.Unexpected`, error code `Transaction.FxRateUnavailable` |

---

## 5. Error Response: 502 Bad Gateway

### Existing error

`TransactionErrors.FxRateUnavailable` is **already defined** in `TransactionErrors.cs`:

```csharp
public static Error FxRateUnavailable => Error.Unexpected(
    "Transaction.FxRateUnavailable",
    "The exchange rate service is temporarily unavailable. Please try again later.");
```

### HTTP mapping problem

`ErrorOrEndpointExtensions.ToHttpResult()` currently maps `ErrorType.Unexpected` → **500 Internal Server Error**:

```csharp
_ => Results.Problem(statusCode: 500, detail: errors[0].Description),
```

502 is the correct semantic for an upstream dependency failure (the CNB API). There are two options:

**Option A — Custom error type (recommended):**  
Use `Error.Custom(type: 502, ...)` from ErrorOr. Add a case in `ErrorOrEndpointExtensions`:
```csharp
// In the switch:
502 => Results.Problem(statusCode: 502, detail: errors[0].Description),
```
Or handle it in the default branch by checking `errors[0].NumericType == 502`.

**Option B — Keep Unexpected → 500:**  
Simpler, minimal code change. The AC says "502 Bad Gateway" but 500 is also acceptable if team prefers not to introduce custom error types yet. Discuss with team.

**Recommendation: Option A.** The intent of 502 (upstream dependency unavailable) is semantically meaningful here and worth modelling explicitly. The change to `ErrorOrEndpointExtensions` is small and tested independently.

### What this means for `TransactionErrors.FxRateUnavailable`

If going with Option A, redefine as:
```csharp
public static Error FxRateUnavailable => Error.Custom(
    type: 502,
    code: "Transaction.FxRateUnavailable",
    description: "The exchange rate service is temporarily unavailable. Please try again later.");
```

And add the 502 case to `ErrorOrEndpointExtensions.ToHttpResult()`:
```csharp
502 => Results.Problem(statusCode: 502, detail: errors[0].Description),
```

---

## 6. Open Questions / Decisions Needed

1. **Option A vs B for 502** — does the team want custom error types in ErrorOr, or is 500 acceptable for now?
2. **`Transaction.FxRateToCzk` nullable or required?** — CZK transactions will always have `null`. Is that fine for reporting queries, or should we store `1.0` for CZK too?
3. **EF migration** — adding `FxRateToCzk` to `Transaction` requires a new migration. Should T-14 own that migration or should it be a separate task?
4. **`IFxRateProvider` in Shared/Services** — this interface already exists with a richer signature (Currency enum + DateOnly). Long-term, should the feature use this abstraction instead of directly depending on `IExchangeRateProvider`? T-14 can defer this and use `IExchangeRateProvider` directly for now.
