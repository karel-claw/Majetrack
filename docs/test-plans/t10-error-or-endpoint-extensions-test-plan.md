# Test Plan: T-10 — ErrorOrEndpointExtensions + ValidateRequest

_Designer: test-scenario-designer | Date: 2026-04-03 | Task: T-10_

---

## Scope

Unit tests for two pure static extension method groups:

1. **`List<Error>.ToHttpResult()`** — maps `ErrorOr` errors to the correct `IResult` HTTP response.
2. **`ErrorOr<T>.ToHttpResult(onValue)`** — convenience overload that routes success through a factory and errors to `List<Error>.ToHttpResult()`.
3. **`ValidateRequest<T>`** — runs FluentValidation and wraps failures as `ErrorOr<T>` validation errors.

**No HTTP pipeline, no DB, no WebApplicationFactory.** All tests are pure in-memory unit tests.

---

## Test Fixture Design

Single test class `ErrorOrEndpointExtensionsTests` with no shared state — each test constructs its own inputs.

### Helper: StubValidator\<T\>

A reusable `AbstractValidator<T>` subclass configured inline per test. No mocking — FluentValidation's real pipeline runs.

```csharp
// Example: validator that rejects empty Symbol and non-positive Amount
var validator = new InlineValidator<CreateRequest>();
validator.RuleFor(x => x.Symbol).NotEmpty();
validator.RuleFor(x => x.Amount).GreaterThan(0);
```

### Concrete IResult type assertions

All assertions use `FluentAssertions` `.BeOfType<T>()` casts to the known ASP.NET Core concrete types:

- `Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult` — for `Results.Problem(...)` calls
- `Microsoft.AspNetCore.Http.HttpResults.ValidationProblem` — for `Results.ValidationProblem(...)` calls

No `HttpContext` or test server is needed.

---

## Error.Type → HTTP Status Mapping (reference)

| Condition | HTTP Status | Concrete Result Type |
|-----------|-------------|---------------------|
| All errors are `Validation` | 400 | `ValidationProblem` |
| First error is `Validation` (mixed list) | 400 | `ProblemHttpResult` |
| First error is `NotFound` | 404 | `ProblemHttpResult` |
| First error is `Conflict` | 409 | `ProblemHttpResult` |
| First error is `Unauthorized` | 401 | `ProblemHttpResult` |
| First error is `Forbidden` | 403 | `ProblemHttpResult` |
| First error is `Unexpected` | 500 | `ProblemHttpResult` |
| First error is `Failure` | 500 | `ProblemHttpResult` |
| Empty list | 500 | `ProblemHttpResult` |
| Any unknown future type | 500 | `ProblemHttpResult` |

---

## Group 1 — `List<Error>.ToHttpResult()`

### TC-01: Single Validation error → ValidationProblem with field key

**Arrange:** `[Error.Validation("Amount", "'Amount' must be greater than 0.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ValidationProblem`
- `ProblemDetails.Status == 400`
- `ProblemDetails.Errors` contains key `"Amount"`
- `ProblemDetails.Errors["Amount"]` contains `"'Amount' must be greater than 0."`

---

### TC-02: Multiple Validation errors, same field → array of both messages under one key

**Arrange:** Two errors with `Code = "Symbol"`, different descriptions: `"'Symbol' must not be empty."` and `"'Symbol' must be 3 characters."`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ValidationProblem`
- `ProblemDetails.Errors["Symbol"]` has exactly 2 items
- Both descriptions present in the array

---

### TC-03: Multiple Validation errors, different fields → all field keys present

**Arrange:** `[Error.Validation("Amount", "msg1"), Error.Validation("Symbol", "msg2")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ValidationProblem`
- `ProblemDetails.Errors` contains key `"Amount"` with `"msg1"`
- `ProblemDetails.Errors` contains key `"Symbol"` with `"msg2"`

---

### TC-04: Mixed errors — first is Validation, second is NotFound → Problem(400), NOT ValidationProblem

**Arrange:** `[Error.Validation("Field", "bad"), Error.NotFound("Entity", "not found")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult` (NOT `ValidationProblem`)
- `ProblemDetails.Status == 400`

_Rationale: `errors.All(...)` is false for mixed lists. The switch's explicit `ErrorType.Validation → 400` case fires instead._

---

### TC-05: NotFound error → Problem(404)

**Arrange:** `[Error.NotFound("Transaction", "Transaction with id 99 was not found.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 404`

---

### TC-06: Conflict error → Problem(409)

**Arrange:** `[Error.Conflict("Transaction", "Duplicate transaction detected.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 409`

---

### TC-07: Unauthorized error → Problem(401)

**Arrange:** `[Error.Unauthorized("Auth", "User is not authenticated.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 401`

---

### TC-08: Forbidden error → Problem(403)

**Arrange:** `[Error.Forbidden("Auth", "User does not have permission.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 403`

_Rationale: Plan review amendment — Forbidden must map to 403, not fall through to 500._

---

### TC-09: Unexpected error → Problem(500)

**Arrange:** `[Error.Unexpected("DB", "Database connection timed out.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 500`

---

### TC-10: Failure error → Problem(500)

**Arrange:** `[Error.Failure("Service", "Downstream service unavailable.")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 500`

---

### TC-11: Empty error list → Problem(500) with "unknown error" detail

**Arrange:** `new List<Error>()`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 500`
- `ProblemDetails.Detail` is not null and contains `"unknown"` (case-insensitive)

_Rationale: Defensive guard for calling-code bugs. ErrorOr guarantees non-empty on failure, but the extension must not throw._

---

### TC-12: Multiple errors, first is NotFound — second being Validation does NOT affect result

**Arrange:** `[Error.NotFound("X", "not found"), Error.Validation("Y", "bad")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult`
- `ProblemDetails.Status == 404`

_Rationale: Only `errors[0].Type` is inspected. Order matters — validates the first-error dispatch contract._

---

### TC-13: Single Validation error preserves the error description as Problem detail

**Arrange:** Mixed list: `[Error.Validation("Field", "specific validation message"), Error.Conflict("X", "conflict")]`
**Act:** `errors.ToHttpResult()`
**Assert:**
- Result is `ProblemHttpResult` (not ValidationProblem, because mixed)
- `ProblemDetails.Status == 400`
- `ProblemDetails.Detail` equals `"specific validation message"` (from `errors[0].Description`)

---

## Group 2 — `ErrorOr<T>.ToHttpResult(onValue)`

### TC-14: Success path — onValue factory is invoked with the correct value

**Arrange:**
```csharp
record Item(int Id, string Name);
ErrorOr<Item> result = new Item(42, "AAPL");
```
**Act:** `result.ToHttpResult(item => Results.Ok(item))`
**Assert:**
- Return value is `Ok<Item>`
- Inner value is the `Item(42, "AAPL")` instance

---

### TC-15: Success path — custom factory returning Results.Created is returned as-is

**Arrange:**
```csharp
ErrorOr<Item> result = new Item(1, "TSLA");
```
**Act:** `result.ToHttpResult(item => Results.Created($"/api/items/{item.Id}", item))`
**Assert:**
- Return value is `Created<Item>`
- Location is `/api/items/1`

---

### TC-16: Error path — delegates to List\<Error\>.ToHttpResult(), returns Problem(404)

**Arrange:**
```csharp
ErrorOr<Item> result = Error.NotFound("Item", "Not found.");
```
**Act:** `result.ToHttpResult(item => Results.Ok(item))`
**Assert:**
- Return value is `ProblemHttpResult`
- `ProblemDetails.Status == 404`
- `onValue` is never invoked (factory lambda should not be called)

_Verification: Use a `bool wasCalled = false` flag in the lambda, assert it remains `false`._

---

### TC-17: Error path with Validation errors → returns ValidationProblem (delegates correctly)

**Arrange:**
```csharp
ErrorOr<Item> result = Error.Validation("Name", "Name is required.");
```
**Act:** `result.ToHttpResult(item => Results.Ok(item))`
**Assert:**
- Return value is `ValidationProblem`
- `ProblemDetails.Errors` contains key `"Name"`

---

## Group 3 — `ValidateRequest<T>`

### TC-18: Valid request → returns ErrorOr with original value (IsError = false)

**Arrange:**
```csharp
record CreateRequest(string Symbol, decimal Amount);
var validator = new InlineValidator<CreateRequest>();
validator.RuleFor(x => x.Symbol).NotEmpty();
validator.RuleFor(x => x.Amount).GreaterThan(0);
var request = new CreateRequest("AAPL", 100m);
```
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.IsError == false`
- `result.Value` is the original `request` instance (reference equality or structural equality)

---

### TC-19: Single invalid field → returns errors with Validation type

**Arrange:** Validator requires non-empty `Symbol`. Request has `Symbol = ""`.
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.IsError == true`
- All errors have `Type == ErrorType.Validation`

---

### TC-20: Multiple invalid fields → all failures present as separate Validation errors

**Arrange:** Validator requires non-empty `Symbol` AND `Amount > 0`. Request: `Symbol = "", Amount = -1`.
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.IsError == true`
- `result.Errors` count is at least 2
- Errors contain one with `Code == "Symbol"` and one with `Code == "Amount"`

---

### TC-21: Error.Code matches FluentValidation PropertyName exactly

**Arrange:**
```csharp
validator.RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Must be positive.");
var request = new CreateRequest("AAPL", 0m);
```
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.Errors.Single().Code == "Amount"` (exact PascalCase match — not "amount")

---

### TC-22: Error.Description matches FluentValidation ErrorMessage exactly

**Arrange:**
```csharp
validator.RuleFor(x => x.Symbol).NotEmpty().WithMessage("Symbol is required.");
var request = new CreateRequest("", 50m);
```
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.Errors.Single().Description == "Symbol is required."` (verbatim match)

---

### TC-23: Multiple failures on the same field → multiple errors with same Code

**Arrange:**
```csharp
validator.RuleFor(x => x.Symbol).NotEmpty().WithMessage("Symbol is required.");
validator.RuleFor(x => x.Symbol).Length(3, 5).WithMessage("Symbol must be 3-5 characters.");
var request = new CreateRequest("", 50m);  // fails both rules
```
**Act:** `await request.ValidateRequest(validator)`
**Assert:**
- `result.IsError == true`
- Both errors have `Code == "Symbol"`
- Descriptions contain both messages

---

### TC-24: Cancellation token is forwarded — cancelled token throws OperationCanceledException

**Arrange:**
```csharp
var validator = new InlineValidator<CreateRequest>();
validator.RuleFor(x => x.Symbol).NotEmpty();
var request = new CreateRequest("AAPL", 100m);
var cts = new CancellationTokenSource();
cts.Cancel();  // pre-cancelled
```
**Act & Assert:**
```csharp
Func<Task> act = async () => await request.ValidateRequest(validator, cts.Token);
await act.Should().ThrowAsync<OperationCanceledException>();
```

_Rationale: Verifies that `ct` is passed to `validator.ValidateAsync(request, ct)`. A pre-cancelled token causes FV to throw immediately. GlobalExceptionHandler (T-09) will convert this to 499._

---

## Out of Scope

| Scenario | Reason |
|----------|--------|
| HTTP pipeline / response serialization | Pure mapping — no transport layer involved |
| Null validator passed to ValidateRequest | Design doc explicitly leaves this unguarded; NullReferenceException is intentional |
| ErrorOr constructed from multiple mixed errors in ErrorOr<T>.ToHttpResult | Covered transitively via TC-16/17 — the delegation is what's tested, not error combinatorics |
| JSON output of ValidationProblem / ProblemDetails | ASP.NET Core serialization — not part of these extensions |

---

## Implementation Notes for Test Author

1. **Concrete result type imports:**
   ```csharp
   using Microsoft.AspNetCore.Http.HttpResults;
   // ValidationProblem, ProblemHttpResult, Ok<T>, Created<T>
   ```

2. **InlineValidator pattern** (FluentValidation built-in):
   ```csharp
   using FluentValidation;
   var v = new InlineValidator<MyRequest>();
   v.RuleFor(x => x.Field).NotEmpty();
   ```

3. **ErrorOr construction for tests:**
   ```csharp
   ErrorOr<Item> success = new Item(1, "X");          // implicit conversion
   ErrorOr<Item> failure = Error.NotFound("X", "Y");  // implicit conversion
   ErrorOr<Item> multiError = new List<Error> { Error.Validation("A", "b"), Error.NotFound("C", "d") };
   ```

4. **FluentAssertions chaining for IResult:**
   ```csharp
   result.Should().BeOfType<ProblemHttpResult>()
         .Which.ProblemDetails.Status.Should().Be(404);

   result.Should().BeOfType<ValidationProblem>()
         .Which.ProblemDetails.Errors.Should().ContainKey("Symbol");
   ```

5. **Test file location:**
   `tests/Majetrack.Features.Tests/Shared/Extensions/ErrorOrEndpointExtensionsTests.cs`

6. **No async lifecycle** — all tests are synchronous or use `await` on `ValidateRequest` only. No IClassFixture needed; no shared state.

---

_Total test cases: 24 (13 ToHttpResult(List<Error>), 4 ErrorOr<T>.ToHttpResult, 7 ValidateRequest)_

_Designer: test-scenario-designer | Date: 2026-04-03 | Task: T-10_
