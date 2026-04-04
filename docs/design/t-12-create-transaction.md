# Design: T-12 POST /api/transactions — CreateTransactionEndpoint

## Summary

Manual transaction entry endpoint supporting all 6 transaction types (Buy, Sell, Deposit, Withdrawal, Interest, Dividend). Validates input, optionally resolves asset and FX rate, persists transaction with UserId from ICurrentUser, returns 201 Created.

## Feature Location

`src/Majetrack.Features/Transactions/Create/`

## Files to Create

| File | Purpose |
|------|---------|
| `CreateTransactionEndpoint.cs` | POST endpoint logic with ErrorOr chain |
| `CreateTransactionRequest.cs` | Request DTO with properties |
| `CreateTransactionValidator.cs` | FluentValidation rules |
| `TransactionErrors.cs` | Error factory methods |
| `CreateTransactionFeature.cs` | Feature auto-registration |

## API Contract

### POST /api/transactions
**Auth:** Bearer JWT (ICurrentUser provides UserId)

#### Request Body
```json
{
  "transactionType": "Buy",
  "transactionDate": "2026-03-15",
  "platform": "Xtb",
  "currency": "CZK",
  "totalAmount": 50000.00,
  "fee": 0,
  "note": "Apple shares",
  "assetId": "550e8400-e29b-41d4-a716-446655440000",
  "quantity": 10,
  "pricePerUnit": 5000.00
}
```

**For Deposit/Withdrawal (no asset):**
```json
{
  "transactionType": "Deposit",
  "transactionDate": "2026-03-15",
  "platform": "Xtb",
  "currency": "CZK",
  "totalAmount": 50000.00,
  "fee": 0
}
```

#### Response 201 Created
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000"
}
```
Location header: `/api/transactions/{id}`

#### Error Responses
- 400 — Validation failure (ProblemDetails with errors)
- 401 — Unauthenticated
- 404 — Asset not found (for Buy/Sell/Interest/Dividend)
- 502 — Bad Gateway (CNB FX rate unavailable)

## Validation Rules

| Field | Rules | Applies To |
|-------|-------|------------|
| transactionType | Required, valid enum | All |
| transactionDate | Required, valid DateOnly | All |
| platform | Required, valid enum | All |
| currency | Required, valid enum | All |
| totalAmount | Required, > 0 | All |
| fee | >= 0 | All |
| note | MaxLength(500) | All |
| assetId | Required, valid GUID | Buy, Sell, Interest, Dividend |
| quantity | Required, > 0 | Buy, Sell, Interest, Dividend |
| pricePerUnit | Required, > 0 | Buy, Sell, Interest, Dividend |

**Conditional validation:**
- Buy/Sell/Interest/Dividend → require assetId, quantity, pricePerUnit
- Deposit/Withdrawal → these fields must be null/empty

## ErrorOr Chain Design

```csharp
// 1. Validation (before chain)
var validation = await request.ValidateAsync(validator);
if (validation.IsFailed) return validation.Errors.ToHttpResult();

// 2. Skip Asset resolution for Deposit/Withdrawal
var asset = request.TransactionType is TransactionType.Deposit or TransactionType.Withdrawal
    ? null
    : await assetRepository.GetByIdAsync(request.AssetId!, ct);
if (asset is null) return TransactionErrors.NotFound(request.AssetId).ToHttpResult();

// 3. FX rate for non-CZK
var fxRate = request.Currency == Currency.CZK
    ? 1m
    : await fxRateProvider.GetRateAsync(request.Currency, Currency.CZK, ct);
if (fxRate is null) return TransactionErrors.FxRateUnavailable().ToHttpResult();

// 4. Persist
var transaction = new Transaction
{
    Id = Guid.NewGuid(),
    UserId = currentUser.UserId,
    AssetId = request.AssetId,
    TransactionType = request.TransactionType,
    TransactionDate = request.TransactionDate,
    Quantity = request.Quantity ?? 1,
    PricePerUnit = request.PricePerUnit ?? request.TotalAmount,
    TotalAmount = request.TotalAmount,
    Currency = request.Currency,
    Platform = request.Platform,
    Fee = request.Fee,
    Note = request.Note,
    CreatedAt = DateTimeOffset.UtcNow
};
await repository.AddAsync(transaction, ct);

// 5. Response
return Results.Created($"/api/transactions/{transaction.Id}", new { id = transaction.Id });
```

## Data Model Changes

**EF Core migration required:** Yes
- New table: `transactions` (if not exists)
- See existing migrations in `src/Majetrack.Infrastructure/Persistence/Migrations/`

## Error Codes to Add

| Error method | Error type | Message |
|-------------|-----------|---------|
| `TransactionErrors.NotFound(assetId)` | NotFound | "Asset with ID '{assetId}' was not found." |
| `TransactionErrors.FxRateUnavailable()` | Custom (502) | "Exchange rate service temporarily unavailable." |

## Security Considerations

- **Auth:** Bearer JWT required (ICurrentUser from middleware)
- **User isolation:** UserId from ICurrentUser, not from request
- **Input validation:** All fields validated server-side

## Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|----------|
| FX rate failure → 502 | Custom error + 502 | T-13/T-14 not implemented; 502 indicates upstream failure |
| Deposit/Withdrawal skip AssetId | Conditional in code | Simpler than separate endpoints |
| Quantity default | 1 for cash txns | Standard for Deposit/Withdrawal |

## Open Questions

1. **Should we validate asset belongs to user?** — Currently no ownership check on Asset entity. TBD if needed.
2. **External ID for deduplication?** — Issue mentions for CSV import, not manual. Not in scope.
3. **What if FX rate stale?** — Cache TTL not defined. T-13/T-14 scope.

---

_Date: 2026-04-04_