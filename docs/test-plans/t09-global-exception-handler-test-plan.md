# Test Plan: T-09 — Global Exception Handler

_Designer: test-scenario-designer | Date: 2026-04-03 | Task: T-09_

---

## Scope

Tests for `GlobalExceptionHandler` — an `IExceptionHandler` implementation that catches all unhandled exceptions and returns an RFC 7807 `ProblemDetails` 500 response.

All tests are **integration tests** using `WebApplicationFactory<Program>`. No unit tests for the handler class itself — it contains no business logic, only HTTP pipeline plumbing.

---

## Test Fixture Design

All tests share a `GlobalExceptionHandlerTests` class using `IClassFixture<T>`.

### TestFactory

`WebApplicationFactory<Program>` subclass that:
- Sets environment to `"Testing"` via `builder.UseEnvironment("Testing")`
- Replaces PostgreSQL with an in-memory EF Core provider (same pattern as `FeatureRegistrationTests`)
- Registers a set of **test-only endpoints** that throw predictable exceptions:

| Route | Behaviour |
|-------|-----------|
| `GET /test/throw/generic` | Throws `new InvalidOperationException("Test error")` |
| `GET /test/throw/argument` | Throws `new ArgumentException("Bad arg")` |
| `GET /test/throw/nullref` | Throws `new NullReferenceException("null was null")` |
| `GET /test/throw/cancelled` | Throws `new OperationCanceledException()` |
| `GET /test/throw/task-cancelled` | Throws `new TaskCanceledException()` |
| `GET /test/throw/started` | Writes `"partial"` to body, then throws `new InvalidOperationException("late throw")` |
| `GET /test/healthy` | Returns `Results.Ok(new { status = "ok" })` — no side effects |

### DevTestFactory

A separate `WebApplicationFactory<Program>` subclass that sets `"Development"` as the environment, used exclusively for the `detail`-field tests.

---

## Test Cases

---

### Group 1 — Response Shape (Happy Path)

#### TC-01: Unhandled exception returns HTTP 500

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Send request
**Assert:** Response status code is `500`

---

#### TC-02: Content-Type is application/problem+json

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Send request
**Assert:** `Content-Type` header equals `application/problem+json`

---

#### TC-03: Response body `status` field is 500

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `status` == `500`

---

#### TC-04: Response body `title` is "An unexpected error occurred."

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `title` == `"An unexpected error occurred."`

---

#### TC-05: Response body `instance` matches the request path

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `instance` == `"/test/throw/generic"`

---

#### TC-06: Response body `extensions.traceId` is present and non-empty

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `extensions["traceId"]` is not null and not empty string

---

### Group 2 — Environment-Gated Detail Field

#### TC-07: `detail` field is NOT present in Testing environment

**Arrange:** `Testing` environment (i.e., NOT Development), `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `detail` property is null or absent in the JSON

_Rationale: Stack traces must not be exposed in non-Development environments._

---

#### TC-08: `detail` field IS present in Development environment

**Arrange:** `Development` environment (uses `DevTestFactory`), `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `detail` is not null and contains the exception type name (`"InvalidOperationException"`) and message (`"Test error"`)

_Rationale: Full stack trace must be visible to developers locally._

---

#### TC-09: `detail` in Development contains full stack trace (not just message)

**Arrange:** `Development` environment, `GET /test/throw/generic`
**Act:** Deserialize response JSON
**Assert:** `detail` contains at least one stack frame indicator (e.g., `"   at "`)

---

### Group 3 — Exception Type Coverage

#### TC-10: `ArgumentException` is caught and returns 500

**Arrange:** `Testing` environment, `GET /test/throw/argument`
**Act:** Send request
**Assert:** Status `500`, `title` == `"An unexpected error occurred."`

---

#### TC-11: `NullReferenceException` is caught and returns 500

**Arrange:** `Testing` environment, `GET /test/throw/nullref`
**Act:** Send request
**Assert:** Status `500`, `title` == `"An unexpected error occurred."`

---

### Group 4 — Cancellation Exception Handling

#### TC-12: `OperationCanceledException` is handled and does NOT log at Error level

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/cancelled`
**Act:** Send request
**Assert:**
- HTTP response status code is `499` (Client Closed Request) — the handler returns a dedicated non-500 status to distinguish client-initiated cancellations from server errors
- No `LogLevel.Error` entry emitted for `OperationCanceledException`
- Either `LogLevel.Information` entry exists, or no log entry at all (suppressed)

_Rationale: Cancelled requests are not bugs — logging them at Error would pollute alerting. Returning 499 (a well-known nginx convention for client-closed) makes cancellations identifiable in logs and APM tools without masking them as server errors. The exact status code (499 vs. 503 vs. swallowed entirely) must be pinned here so the implementation has a clear contract._

---

#### TC-13: `TaskCanceledException` is handled and does NOT log at Error level

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/task-cancelled`
**Act:** Send request
**Assert:**
- HTTP response status code is `499` (same contract as TC-12 — `TaskCanceledException` is a subtype of `OperationCanceledException` and must be treated identically)
- No `LogLevel.Error` entry for `TaskCanceledException`
- Either `LogLevel.Information` or suppressed

---

### Group 5 — Logging Verification (Regular Exceptions)

#### TC-14: Regular exception is logged at Error level

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/generic`
**Act:** Send request
**Assert:** Exactly one `LogLevel.Error` log entry captured that references the exception

---

#### TC-15: Log entry includes HTTP method

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/generic`
**Act:** Send request
**Assert:** The captured log message or structured properties include the HTTP method (`"GET"`)

---

#### TC-16: Log entry includes request path

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/generic`
**Act:** Send request
**Assert:** The captured log entry includes the path `/test/throw/generic`

---

#### TC-17: Log entry includes `TraceId`

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/generic`
**Act:** Send request
**Assert:** The captured log entry includes the `TraceIdentifier` value from `HttpContext`

---

#### TC-18: Full exception object (including stack trace) is in the log

**Arrange:** `Testing` environment, capturing logger, `GET /test/throw/generic`
**Act:** Send request
**Assert:** The captured log entry's `Exception` property is not null and equals the thrown `InvalidOperationException`

_Rationale: Stack traces must always be in logs (protected) regardless of environment._

---

### Group 6 — Response.HasStarted Guard

#### TC-19: Handler returns gracefully when response has already started

**Arrange:** Register a test endpoint `GET /test/throw/started` in `TestFactory` that:
1. Sets `Content-Type: text/plain` and writes at least one byte to the response body (e.g., `await response.WriteAsync("partial")`)
2. Then throws `new InvalidOperationException("late throw")`

**Act:** Send request using `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` so partial content is observable.
**Assert:**
- The HTTP call completes without throwing `HttpRequestException` (no unhandled server crash leaks to the client)
- The response does **not** have `Content-Type: application/problem+json` (the handler did not attempt to overwrite an already-started response)
- The response body begins with `"partial"` (the bytes written before the throw are still present — the connection was not reset mid-stream)
- The connection closes normally after the partial body (no TCP RST / `IOException` reading to end of stream)

_Rationale: Writing to a started response would corrupt it. The handler must detect `Response.HasStarted == true`, log the exception, and return without writing a new body. The client receives whatever was written before the throw and then a clean EOF — not a 500 ProblemDetails body appended to the partial content._

---

### Group 7 — traceId Correlation

#### TC-20: `traceId` in response matches the request's `TraceIdentifier`

**Arrange:** `Testing` environment, `GET /test/throw/generic`
**Act:** Send request; also capture `HttpContext.TraceIdentifier` from test endpoint (inject via response header for verification, or via `Activity.Current?.Id`)
**Assert:** `extensions["traceId"]` in ProblemDetails matches the captured identifier

_Note: If capturing the server-side `TraceIdentifier` is not straightforward, assert that `traceId` is non-empty and conforms to the W3C TraceParent format — this is sufficient to confirm it is the correct opaque ID._

---

### Group 8 — Middleware Ordering

#### TC-21: Exception thrown in a feature endpoint is caught (not just test endpoints)

**Arrange:** Temporarily register a real feature endpoint or extend the test factory to add a route under `/api/test-throw` that throws. Send request to that route.
**Act:** Send request
**Assert:** Response is `500` with `application/problem+json`

_Rationale: Confirms that `UseExceptionHandler()` is wired before feature mapping, not after._

---

#### TC-22: Normal requests are unaffected (no false 500s)

**Arrange:** `Testing` environment. `TestFactory` registers a purpose-built route `GET /test/healthy` that returns `Results.Ok(new { status = "ok" })` — no auth, no DB, no side effects.
**Act:** Send request to `GET /test/healthy`
**Assert:** Status code is `200`

_Rationale: Regression guard — exception handler must be transparent for successful requests. A dedicated route is used instead of a real feature endpoint (e.g., `GET /api/transactions`) because real endpoints may require authentication, have their own validation errors, or not yet exist — all of which would produce non-500 failures for unrelated reasons and obscure what is actually being tested._

---

## Out of Scope

| Scenario | Reason |
|----------|--------|
| Unit tests for `GlobalExceptionHandler` in isolation | No business logic; purely HTTP plumbing. Integration tests give better signal. |
| DB-layer exceptions (EF Core failures) | T-09 is a catch-all — no special handling per exception source. Covered by TC-10/11. |
| Auth / 401 / 403 responses | Handler fires for unhandled exceptions only; 401/403 are normal control flow via ASP.NET middleware. |
| Multiple chained `IExceptionHandler` instances | No other handler exists in this project; single-handler chain is the only scenario. |
| Performance / load testing | Out of scope for this task. |

---

## Implementation Notes for Test Author

1. **Capturing logger:** Inject a custom `ILoggerProvider` that buffers `LogEntry` objects. Register via `builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider, CapturingLoggerProvider>())`.
2. **DevTestFactory:** Must also skip migrations (the `IsDevelopment()` guard in `Program.cs` runs `MigrateAsync` — replace DB with in-memory to avoid that).
3. **JSON deserialization:** Use `System.Text.Json` with `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`. `ProblemDetails` has `extensions` as `Dictionary<string, JsonElement>`.
4. **TC-19 (HasStarted guard):** Use `GET /test/throw/started` registered in `TestFactory`. Send the request with `HttpCompletionOption.ResponseHeadersRead`. Read the response body to end-of-stream — expect no `IOException`. Assert body starts with `"partial"` and `Content-Type` is not `application/problem+json`.
5. **Test isolation:** Each exception type should use its own dedicated route so tests can run in any order without shared state.

---

_Total test cases: 22 (6 response shape, 3 env-gating, 2 exception types, 2 cancellation, 5 logging, 1 HasStarted, 2 middleware, 1 correlation)_
