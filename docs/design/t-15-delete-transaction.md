# Design: T-15 DELETE /api/transactions/{id} — DeleteTransaction

**Status:** Design only — no code yet  
**Depends on:** T-12 (Transaction entity, persistence, TransactionErrors)  
**Author:** Karel (architect subagent)

---

## 1. Summary

Delete a transaction by ID. The endpoint verifies ownership (UserId must match ICurrentUser), removes the record, and returns 204 No Content. Non-existent or unauthorized transactions return 404 (same response to prevent information leakage).

**Hard delete** — no soft-delete pattern exists in the codebase. Adding `IsDeleted`/`DeletedAt` would require a global query filter, changes to every existing query, and a migration. Not justified for MVP. If soft-delete is needed later, it can be retrofitted via an EF global filter without changing the API contract (204 stays 204).

---

## 2. Feature Location

`src/Majetrack.Features/Transactions/Delete/`

## 3. Files to Create

| File | Purpose |
|------|---------|
| `DeleteTransactionEndpoint.cs` | DELETE endpoint handler, maps ErrorOr → IResult |
| `DeleteTransactionFeature.cs` | Orchestrates auth → load → ownership check → delete |

## 4. Files to Modify

| File | Change |
|------|--------|
| `Transactions/TransactionsFeature.cs` | Register `DeleteTransactionFeature` in DI; map `DELETE "/{id:guid}"` route |
| `Transactions/Create/TransactionErrors.cs` | Move to `Transactions/TransactionErrors.cs` (shared across Create + Delete) **OR** add delete-specific errors alongside existing ones — see §8 |

No new entity properties or migrations required.

---

## 5. API Contract

### DELETE /api/transactions/{id}
**Auth:** Bearer JWT (RequireAuthorization)

#### Path Parameters

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | Guid | Yes | Transaction ID to delete |

#### Responses

| Status | When | Body |
|--------|------|------|
| 204 No Content | Transaction deleted successfully | None |
| 401 Unauthorized | Missing/invalid JWT | ProblemDetails |
| 404 Not Found | Transaction doesn't exist **or** belongs to another user | ProblemDetails |

No request body. No query parameters.

---

## 6. ErrorOr Chain

```
Step 1: Authenticate       → if UserId is null → TransactionErrors.Unauthenticated (401)
Step 2: Load transaction   → db.Transactions.FirstOrDefaultAsync(t => t.Id == id)
                           → if null → TransactionErrors.TransactionNotFound (404)
Step 3: Ownership check    → if transaction.UserId != currentUser.UserId
                           → TransactionErrors.TransactionNotFound (404, same error — no info leakage)
Step 4: Delete             → db.Transactions.Remove(transaction)
                           → await db.SaveChangesAsync(ct)
Step 5: Return             → ErrorOr<Deleted>.From(Result.Deleted)
```

### Pseudocode

```csharp
public async Task<ErrorOr<Deleted>> ExecuteAsync(Guid id, CancellationToken ct = default)
{
    // Step 1: Authenticate
    if (_currentUser.UserId is not { } userId)
        return TransactionErrors.Unauthenticated;

    // Step 2: Load
    var transaction = await _db.Transactions
        .FirstOrDefaultAsync(t => t.Id == id, ct);

    // Step 3: Not found OR wrong user → same 404
    if (transaction is null || transaction.UserId != userId)
        return TransactionErrors.TransactionNotFound;

    // Step 4: Hard delete
    _db.Transactions.Remove(transaction);
    await _db.SaveChangesAsync(ct);

    // Step 5: Signal success
    return Result.Deleted;
}
```

### Endpoint handler

```csharp
public static async Task<IResult> HandleAsync(
    Guid id,
    DeleteTransactionFeature feature,
    CancellationToken ct)
{
    var result = await feature.ExecuteAsync(id, ct);

    return result.ToHttpResult(_ => Results.NoContent());
}
```

---

## 7. Errors to Add

| Error | ErrorType | HTTP | Message |
|-------|-----------|------|---------|
| `TransactionErrors.TransactionNotFound` | NotFound | 404 | "The specified transaction was not found." |

`TransactionErrors.Unauthenticated` already exists and is reused.

The existing `AssetNotFound` is for assets — we need a separate `TransactionNotFound` for clarity in error codes:
- `Transaction.AssetNotFound` — asset lookup failed during create
- `Transaction.TransactionNotFound` — transaction lookup failed during delete (and future get/update)

---

## 8. DI Registration & Route Mapping

In `TransactionsFeature.cs`:

```csharp
// AddServices
services.AddScoped<DeleteTransactionFeature>();

// MapEndpoints
group.MapDelete("/{id:guid}", DeleteTransactionEndpoint.HandleAsync)
     .RequireAuthorization();
```

---

## 9. Test Cases

| TC | Scenario | Setup | Expected |
|----|----------|-------|----------|
| TC101 | Delete own transaction | Valid user, transaction exists with matching UserId | 204, transaction removed from DB |
| TC102 | Transaction not found | Valid user, random Guid | 404 |
| TC103 | Transaction belongs to other user | Valid user, transaction.UserId ≠ currentUser | 404 (same as not found) |
| TC104 | Unauthenticated | No UserId from ICurrentUser | 401 |
| TC105 | Delete is idempotent-safe | Delete same ID twice | First → 204, second → 404 |

---

## 10. Cascade to Position Calculations

The AC mentions "Cascade to position calculations." Currently there is no position calculation system in the codebase. Two options:

**Option A — Defer (recommended for MVP):** Document that position recalculation is out of scope for T-15. When a position engine is built, it will need to react to transaction deletions (event, direct recalc, or materialized view refresh).

**Option B — Publish domain event:** Add a `TransactionDeleted` event that future subscribers can handle. This is over-engineering for now — there are no event handlers, no MediatR, no outbox.

**Recommendation:** Option A. Hard delete + no cascade. Revisit when positions feature is built.

---

## 11. Open Questions

1. **Hard vs soft delete?** — Recommending hard delete. No soft-delete infrastructure exists. The API contract (204/404) works for both, so switching later is non-breaking. **Decision needed from Honza.**

2. **TransactionErrors location** — Currently errors live in `Transactions/Create/TransactionErrors.cs`. With Delete needing shared errors, should we move to `Transactions/TransactionErrors.cs`? Minor refactor but cleaner. **Recommend: yes, move it.**

3. **Position cascade** — No position engine exists yet. Should T-15 include a TODO/placeholder, or is the AC aspirational? **Recommend: skip for now, add when positions feature lands.**

4. **Concurrency** — If two requests delete the same transaction simultaneously, the second `SaveChangesAsync` will throw `DbUpdateConcurrencyException`. Should we catch and return 404, or let it bubble as 500? **Recommend: catch → 404** (transaction was already gone).

---

_Date: 2026-04-04_
