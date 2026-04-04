# Design: T-16 GET /api/transactions — List with Filters + Pagination

**Status:** Design only — no code yet  
**Depends on:** T-12 (Transaction entity, persistence, TransactionErrors)  
**Author:** Karel (architect subagent)

---

## 1. Summary

Paginated listing endpoint for the current user's transactions. Supports filtering by transaction type, currency, platform, date range, and asset. Returns a page of transactions ordered by `TransactionDate` descending (newest first), with total count for client-side pagination UI.

All queries are scoped to `ICurrentUser.UserId` — users never see other users' data.

---

## 2. Feature Location

`src/Majetrack.Features/Transactions/List/`

## 3. Files to Create

| File | Purpose |
|------|---------|
| `ListTransactionsEndpoint.cs` | GET handler — binds query params, calls feature, maps result → IResult |
| `ListTransactionsFeature.cs` | Orchestrates: auth check → build query → execute → project → return |
| `ListTransactionsQuery.cs` | Query DTO with `[AsParameters]` — all filter + pagination props |
| `TransactionListResponse.cs` | Paginated envelope DTO (`items`, `page`, `pageSize`, `totalCount`, `totalPages`) |
| `TransactionItemResponse.cs` | Single transaction DTO (flat record) |

## 4. Files to Modify

| File | Change |
|------|--------|
| `Transactions/TransactionsFeature.cs` | Register `ListTransactionsFeature` in DI; map `GET "/"` route |

No migrations. No new domain entities.

---

## 5. API Contract

### GET /api/transactions
**Auth:** Bearer JWT (RequireAuthorization)

#### Query Parameters

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `page` | int | No | 1 | 1-based page number |
| `pageSize` | int | No | 20 | Items per page (1–100) |
| `transactionType` | string | No | — | Filter by enum name: `Buy`, `Sell`, `Deposit`, `Withdrawal`, `Interest`, `Dividend` |
| `currency` | string | No | — | Filter by enum name: `CZK`, `EUR`, `USD` |
| `platform` | string | No | — | Filter by enum name: `Xtb`, `Etoro`, `Investown` |
| `dateFrom` | DateOnly | No | — | Inclusive lower bound on `TransactionDate` |
| `dateTo` | DateOnly | No | — | Inclusive upper bound on `TransactionDate` |
| `assetId` | Guid | No | — | Filter by specific asset |

All filter params are optional. When omitted, no filtering is applied for that dimension. Multiple filters combine with AND logic.

#### Query DTO

```csharp
public record ListTransactionsQuery(
    int? Page,
    int? PageSize,
    TransactionType? TransactionType,
    Currency? Currency,
    Platform? Platform,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    Guid? AssetId);
```

Decorated with `[AsParameters]` on the endpoint, matching the pattern from `AssetListQuery`.

---

## 6. Response

### 200 OK

```json
{
  "items": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "transactionType": "Buy",
      "transactionDate": "2026-03-15",
      "platform": "Xtb",
      "currency": "CZK",
      "totalAmount": 50000.00,
      "fee": 0,
      "quantity": 10,
      "pricePerUnit": 5000.00,
      "assetId": "660e8400-e29b-41d4-a716-446655440001",
      "note": "Apple shares",
      "createdAt": "2026-03-15T10:30:00+00:00"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 150,
  "totalPages": 8
}
```

#### Response DTOs

```csharp
public record TransactionListResponse(
    IReadOnlyList<TransactionItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record TransactionItemResponse(
    Guid Id,
    string TransactionType,
    DateOnly TransactionDate,
    string Platform,
    string Currency,
    decimal TotalAmount,
    decimal Fee,
    decimal Quantity,
    decimal PricePerUnit,
    Guid? AssetId,
    string? Note,
    DateTimeOffset CreatedAt);
```

#### Error Responses

| Status | When |
|--------|------|
| 400 Bad Request | Invalid query params (e.g. `page=0`, `pageSize=200`, bad enum value) |
| 401 Unauthorized | Missing/invalid JWT |

No 404 — empty result set returns 200 with `items: []` and `totalCount: 0`.

---

## 7. Feature Logic (Pseudocode)

```csharp
public async Task<ErrorOr<TransactionListResponse>> ExecuteAsync(
    ListTransactionsQuery q, CancellationToken ct)
{
    if (_currentUser.UserId is not { } userId)
        return TransactionErrors.Unauthenticated;

    var page = Math.Max(q.Page ?? 1, 1);
    var pageSize = Math.Clamp(q.PageSize ?? 20, 1, 100);

    var query = _db.Transactions
        .AsNoTracking()
        .Where(t => t.UserId == userId);

    // Apply filters
    if (q.TransactionType is { } tt)
        query = query.Where(t => t.TransactionType == tt);
    if (q.Currency is { } cur)
        query = query.Where(t => t.Currency == cur);
    if (q.Platform is { } plat)
        query = query.Where(t => t.Platform == plat);
    if (q.DateFrom is { } from)
        query = query.Where(t => t.TransactionDate >= from);
    if (q.DateTo is { } to)
        query = query.Where(t => t.TransactionDate <= to);
    if (q.AssetId is { } aid)
        query = query.Where(t => t.AssetId == aid);

    // Count + paginate
    var totalCount = await query.CountAsync(ct);
    var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

    var items = await query
        .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new TransactionItemResponse(
            t.Id,
            t.TransactionType.ToString(),
            t.TransactionDate,
            t.Platform.ToString(),
            t.Currency.ToString(),
            t.TotalAmount,
            t.Fee,
            t.Quantity,
            t.PricePerUnit,
            t.AssetId,
            t.Note,
            t.CreatedAt))
        .ToListAsync(ct);

    return new TransactionListResponse(items, page, pageSize, totalCount, totalPages);
}
```

### Sorting

Default sort: `TransactionDate DESC, CreatedAt DESC` — newest transactions first. Secondary sort by `CreatedAt` breaks ties when multiple transactions share the same date.

No user-configurable sorting in this ticket. Can be added later with `sortBy` + `sortDirection` params if needed.

---

## 8. Validation

Validation is lightweight — no FluentValidation needed. Clamping handles edge cases:

| Param | Handling |
|-------|----------|
| `page < 1` | Clamped to 1 |
| `pageSize < 1` or `> 100` | Clamped to 1–100 range |
| Invalid enum string | ASP.NET model binding returns 400 automatically |
| `dateFrom > dateTo` | Returns empty result (valid but zero matches) — not an error |

---

## 9. Performance Considerations

- **Index:** `transactions` table should have an index on `(UserId, TransactionDate DESC)` to support the primary query pattern. Check if the existing migration covers this; if not, add a non-breaking migration.
- **Two queries:** `COUNT(*)` + paginated `SELECT` — standard pattern, acceptable at MVP scale. For large datasets, consider cursor-based pagination later.
- **AsNoTracking:** Always — read-only endpoint.

---

## 10. Test Cases

| TC | Scenario | Expected |
|----|----------|----------|
| TC201 | No filters, default pagination | 200, first 20 items, correct totalCount/totalPages |
| TC202 | Filter by transactionType=Buy | Only Buy transactions returned |
| TC203 | Filter by dateFrom + dateTo | Only transactions within date range |
| TC204 | Filter by assetId | Only transactions for that asset |
| TC205 | Filter by platform + currency (combined) | AND logic applied |
| TC206 | page=2, pageSize=5 | Correct offset, items[0] is 6th transaction |
| TC207 | page beyond totalPages | 200 with empty items[], correct totalCount |
| TC208 | pageSize=0 → clamped to 1 | Returns 1 item |
| TC209 | pageSize=200 → clamped to 100 | Returns max 100 items |
| TC210 | No transactions exist | 200, items=[], totalCount=0, totalPages=0 |
| TC211 | Unauthenticated | 401 |
| TC212 | User A cannot see User B's transactions | Only own transactions returned |
| TC213 | Invalid enum value (e.g. transactionType=Foo) | 400 from model binding |

---

## 11. Open Questions

1. **Sorting flexibility** — Should the endpoint support `sortBy` / `sortDirection` query params, or is `TransactionDate DESC` sufficient for MVP? **Recommend: hardcoded sort for now**, add sortable columns later if UI needs it.

2. **Index on (UserId, TransactionDate)** — Does the current migration include a composite index? If not, we need a new migration. Non-breaking, but needs to be part of the implementation. **Action: check existing index during implementation.**

3. **Asset name in response** — Should each `TransactionItemResponse` include the asset's name/ticker via a JOIN, or should the client resolve it separately? Including it avoids N+1 on the frontend. **Recommend: include `assetTicker` and `assetName` via left join** — small cost, big UX win. Decision needed.

4. **Clamping vs 400 for bad page/pageSize** — Current design silently clamps invalid values. Alternative: return 400. **Recommend: clamp** — friendlier for frontend, no real security concern.

5. **ExternalId in response** — The `Transaction` entity has `ExternalId` (for CSV import dedup). Should it be exposed in the list response? **Recommend: omit for now** — internal plumbing, not user-facing.

---

_Date: 2026-04-04_
