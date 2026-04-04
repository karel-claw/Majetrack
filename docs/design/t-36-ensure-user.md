# Design: T-36 EnsureUserMiddleware — Auto-provision User from Entra Token

**Status:** Design only — no code yet  
**Depends on:** Authentication configured (Entra ID / JWT bearer)  
**Author:** Karel (architect subagent)

---

## 1. Summary

ASP.NET Core middleware that runs after `UseAuthentication()` + `UseAuthorization()` and ensures every authenticated request has a corresponding `User` record in the database. If the user doesn't exist yet, it creates one from the Entra ID token claims. If the user exists, it updates `LastLoginAt`.

This eliminates the need for a separate registration endpoint — the first authenticated API call auto-provisions the user.

---

## 2. When Triggered

### Pipeline Position

```
app.UseExceptionHandler();        // existing
app.UseHttpsRedirection();        // existing
app.UseAuthentication();           // existing
app.UseAuthorization();            // existing
app.UseMiddleware<EnsureUserMiddleware>();  // ← NEW — after auth
app.MapFeatures(featuresAssembly); // existing
```

### Trigger Conditions

The middleware executes its logic **only when all of these are true:**

1. `HttpContext.User.Identity?.IsAuthenticated == true` — request carries a valid Entra token
2. The `oid` claim (Entra Object ID) is present in the token

If the request is **unauthenticated** (anonymous endpoint, no token, invalid token), the middleware calls `next(context)` immediately — zero overhead, no DB call.

### Required Claims

| Claim | JWT claim type | Purpose |
|-------|---------------|---------|
| Object ID | `http://schemas.microsoft.com/identity/claims/objectidentifier` (or `oid`) | Unique Entra user identifier → `User.EntraObjectId` |
| Email | `preferred_username` or `email` | → `User.Email` |
| Display name | `name` | → `User.DisplayName` |

---

## 3. User Creation Logic

### Flow

```
Request arrives (authenticated)
  │
  ├─ Extract `oid` claim from token
  │   └─ Missing? → 401 Unauthorized (invalid token shape)
  │
  ├─ Query: SELECT user WHERE entra_object_id = @oid
  │
  ├─ User EXISTS:
  │   ├─ Update LastLoginAt = DateTimeOffset.UtcNow
  │   ├─ SaveChanges
  │   ├─ Store User.Id in HttpContext.Items["UserId"]
  │   └─ next(context)
  │
  └─ User DOES NOT EXIST:
      ├─ Create new User {
      │     Id = Guid.NewGuid(),
      │     EntraObjectId = oid,
      │     Email = email claim,
      │     DisplayName = name claim,
      │     CreatedAt = DateTimeOffset.UtcNow,
      │     LastLoginAt = DateTimeOffset.UtcNow
      │  }
      ├─ DbContext.Users.Add(user)
      ├─ SaveChanges
      ├─ Store User.Id in HttpContext.Items["UserId"]
      └─ next(context)
```

### HttpContext.Items for Downstream Access

The middleware stores the resolved `User.Id` (Guid) in `HttpContext.Items["UserId"]` so that feature endpoints can retrieve the current user's internal ID without repeating the DB lookup. A thin extension method (e.g., `HttpContext.GetUserId()`) provides typed access.

### Race Condition: Concurrent First Requests

Two simultaneous requests from a brand-new user could both see "user not found" and try to INSERT. The unique index on `entra_object_id` prevents duplicates.

**Strategy:** Catch `DbUpdateException` with a unique constraint violation on insert, then retry the SELECT. Simple, no distributed locks, leverages the DB as source of truth.

```
try INSERT
catch DbUpdateException (unique violation)
  → SELECT again (the other request won)
  → continue with existing user
```

---

## 4. File Location

```
src/Majetrack.Api/Infrastructure/EnsureUserMiddleware.cs
```

Follows the existing convention — `GlobalExceptionHandler` is already in `Infrastructure/`.

---

## 5. Dependencies

| Dependency | How |
|-----------|-----|
| `MajetrackDbContext` | Injected via `RequestServices` (scoped per-request). **Not** constructor-injected — middleware is singleton, DbContext is scoped. |
| `ILogger<EnsureUserMiddleware>` | Constructor-injected (singleton-safe) |

---

## 6. Error Handling

| Scenario | Behavior |
|----------|----------|
| **Missing `oid` claim** (authenticated but weird token) | Return 401 with ProblemDetails. Log warning. Do not call `next()`. |
| **Missing `email` or `name` claims** | Use fallback: email → `"unknown@entra"`, name → `"Unknown User"`. Log warning. Still create user — better to have a record with partial data than to block access. |
| **DB unreachable** | Exception propagates up to `GlobalExceptionHandler` → 500. Middleware does not swallow DB errors. |
| **Unique constraint violation (race)** | Caught and retried with SELECT as described above. Not an error. Log at Debug level. |
| **SaveChanges fails for other reasons** | Propagates to `GlobalExceptionHandler` → 500. |

---

## 7. Performance Considerations

- **Every authenticated request hits the DB** — a SELECT by indexed column (`entra_object_id` has a unique index). This is fast (sub-ms on PostgreSQL).
- **Future optimization:** If this becomes a bottleneck, add a short-lived in-memory cache (e.g., 60s TTL keyed by `oid` → `User.Id`). Not needed now — premature optimization. The `LastLoginAt` update can be made eventual (batched/debounced) if write volume becomes an issue.
- **Unauthenticated requests:** Zero overhead — early return before any DB call.

---

## 8. Registration via Middleware

### Pipeline registration in `Program.cs`

```
// After UseAuthorization(), before MapFeatures()
app.UseMiddleware<EnsureUserMiddleware>();
```

No DI registration needed — the middleware class itself is resolved by the framework. It pulls `MajetrackDbContext` from `HttpContext.RequestServices` per-request.

---

## 9. Acceptance Criteria

| Criterion | Verification |
|-----------|-------------|
| First authenticated request creates a User record | Integration test: call any endpoint with valid token → User exists in DB |
| Subsequent requests update LastLoginAt | Integration test: two requests → LastLoginAt changes |
| Unauthenticated requests pass through untouched | Integration test: anonymous request → no User created, endpoint responds normally |
| Concurrent first requests don't create duplicates | Integration test: parallel requests with same OID → exactly one User record |
| User.Id available to downstream endpoints via HttpContext | Unit test: middleware sets `Items["UserId"]` |

---

## 10. Open Questions

1. **Should `LastLoginAt` update on every single request?** — Current design says yes for simplicity. If write volume is a concern, we could throttle updates (e.g., only update if last update was >5 min ago). Decision: start simple, optimize if needed.

2. **Profile updates from token?** — If the user changes their display name or email in Entra, should the middleware update the local record? Current design: no — keep it simple, first-write wins. A future "sync profile" feature can handle this.

3. **`GetUserId()` extension method** — should it throw if UserId is missing from Items (meaning middleware didn't run), or return null? Recommendation: throw `InvalidOperationException` — if you're calling this on an authenticated endpoint, the middleware must have run. A null means a bug.

---

_Date: 2026-04-04_
