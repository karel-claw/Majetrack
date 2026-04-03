# Design: T-09 — Global Exception Handler

## Summary

Adds a centralized, unhandled-exception handler that converts any uncaught exception into an RFC 7807 `ProblemDetails` 500 response. Stack traces are exposed only in the Development environment; all exceptions are logged via `ILogger` before responding.

---

## Feature Location

Infrastructure concern, not a feature slice. Lives in `Majetrack.Api` — it is pure HTTP-pipeline wiring.

---

## Files to Create / Modify

| Action   | File                                                          | Purpose                                               |
|----------|---------------------------------------------------------------|-------------------------------------------------------|
| **Create** | `src/Majetrack.Api/Infrastructure/GlobalExceptionHandler.cs` | `IExceptionHandler` implementation — logs and writes ProblemDetails |
| **Modify** | `src/Majetrack.Api/Program.cs`                               | Register and activate the handler in the middleware pipeline |

No new folders are needed in the Features or Infrastructure projects — exception handling is an API-layer concern.

---

## Implementation Approach

### Choice: `IExceptionHandler` (ASP.NET Core 8+) — **selected**

Three options were evaluated:

| Option | Pros | Cons |
|--------|------|------|
| **`IExceptionHandler` interface** | Strongly typed, DI-friendly, independently testable, can chain multiple handlers | Requires two registration calls (`AddExceptionHandler` + `UseExceptionHandler`) |
| `UseExceptionHandler` lambda (issue sketch) | One-liner, minimal boilerplate | Injects infrastructure logic into Program.cs; not unit-testable; harder to extend |
| Custom `IMiddleware` class | Maximum control, explicit ordering | Boilerplate: manual status code management, no built-in ProblemDetails integration |

`IExceptionHandler` is the **official ASP.NET Core 8+ pattern** for structured exception handling. It is trivially DI-injectable, independently testable, and leaves Program.cs clean. The project targets .NET 10, so no compatibility concern.

### Handler contract

`IExceptionHandler.TryHandleAsync` returns `true` if the handler processed the exception (short-circuits the chain) or `false` to pass to the next registered handler. Our handler always returns `true` — it is the terminal catch-all.

### Conditional detail exposure

`IHostEnvironment` is injected via the primary constructor. `env.IsDevelopment()` guards whether `exception.ToString()` (full stack trace + inner exceptions) is placed in `ProblemDetails.Detail`. In non-Development environments `Detail` is `null`.

---

## Middleware Pipeline Integration

`UseExceptionHandler()` **must be the outermost middleware** so it wraps every subsequent middleware and endpoint. In Program.cs it goes immediately after `app.Build()`, before anything else:

```
app.UseExceptionHandler()         ← first (catches all downstream exceptions)
app.UseHttpsRedirection()
app.UseAuthentication()
app.UseAuthorization()
app.MapFeatures(...)
```

In addition, **`AddProblemDetails()`** must be registered in the service container so the ASP.NET Core infrastructure knows to use the ProblemDetails format for built-in error responses (e.g. 404 from routing). `AddExceptionHandler<GlobalExceptionHandler>()` registers our handler.

> **Note on Development:** The existing `UseSwagger` / `UseSwaggerUI` / `MigrateAsync` block must remain inside the `IsDevelopment()` guard — `UseExceptionHandler` is added unconditionally outside that block.

---

## Logging Strategy

| What | Level | Rationale |
|------|-------|-----------|
| Every unhandled exception | `Error` | Unhandled exceptions indicate bugs that must be investigated |
| Structured fields: `{Method}`, `{Path}`, `{TraceId}` | — | Enables log correlation by request identity |
| Full `Exception` object passed to `LogError` | — | Full stack trace in logs regardless of environment; `Detail` in response is the only thing gated |

The `TraceIdentifier` from `HttpContext` is included in the log message so that client-visible `traceId` in the ProblemDetails response can be correlated back to log entries without leaking the stack trace.

The `ProblemDetails.Extensions` dictionary will include `"traceId": httpContext.TraceIdentifier` in all environments — this is safe (it is an opaque ID, not a stack trace) and enables support teams to query logs.

---

## ProblemDetails Response Shape

**All environments:**
```json
{
  "status": 500,
  "title": "An unexpected error occurred.",
  "instance": "/api/some/path",
  "extensions": {
    "traceId": "00-abc123-def456-00"
  }
}
```

**Development only — `detail` field added:**
```json
{
  "status": 500,
  "title": "An unexpected error occurred.",
  "detail": "System.InvalidOperationException: Something went wrong\n   at ...",
  "instance": "/api/some/path",
  "extensions": {
    "traceId": "00-abc123-def456-00"
  }
}
```

---

## Service Registration Changes (Program.cs)

```csharp
// In the builder section — add alongside existing services:
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// In the app section — first middleware call:
app.UseExceptionHandler();
```

`AddProblemDetails()` is a no-cost registration; it does not conflict with Swagger or any existing service.

---

## Data Model Changes

None. No DB changes, no migrations.

---

## NuGet Packages

None. `IExceptionHandler`, `ProblemDetails`, and all required types ship with `Microsoft.AspNetCore.App` (included via `Microsoft.NET.Sdk.Web`). No additional packages needed.

---

## Security Considerations

- Stack traces contain implementation details (class names, file paths, line numbers) that aid attackers. The `env.IsDevelopment()` guard is the only control. It relies on `ASPNETCORE_ENVIRONMENT` being set correctly in each deployment environment — this must be enforced at the infrastructure level (container env vars / app service config).
- `traceId` is safe to expose in all environments — it contains no application secrets or structural information.
- Do not log request bodies inside the handler (risk of logging PII / credentials from malformed requests).

---

## Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Interface approach | `IExceptionHandler` over lambda | Testable, DI-injectable, idiomatic for .NET 8+ |
| Where to live | `Majetrack.Api/Infrastructure/` | Not a feature slice; pure HTTP pipeline infrastructure |
| Detail gating | `env.IsDevelopment()` (runtime) | Compile-time switches would break integration test containers |
| Stack trace in logs | Always (all environments) | Logs are protected; HTTP response is public-facing |
| `traceId` in response | Always (all environments) | Safe identifier; enables support correlation without leaking secrets |

---

## Open Questions

None — requirements are fully specified and unambiguous.

---

_Architect: claude-sonnet-4-6 | Date: 2026-04-03 | Task: T-09_
