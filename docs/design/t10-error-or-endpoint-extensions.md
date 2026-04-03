# Design: T-10 — ErrorOrEndpointExtensions + ValidateRequest Helper

## Summary

Shared extension methods used by every endpoint in the Features project. Two concerns:

1. **`ErrorOrEndpointExtensions`** — maps `ErrorOr<T>` / `List<Error>` to the correct `IResult` HTTP response.
2. **`ValidateRequest`** — runs a FluentValidation validator against a request object and wraps the result as `ErrorOr<T>`.

Both live in a single file. Together they form the standard endpoint authoring pattern:

```csharp
var result = await request.ValidateRequest(validator, ct);
return result.Match(
    value => Results.Ok(value),
    errors => errors.ToHttpResult());
```

---

## Files to Create / Modify

| Action | File | Purpose |
|--------|------|---------|
| **Create** | `src/Majetrack.Features/Shared/Extensions/ErrorOrEndpointExtensions.cs` | Both extension method groups |
| **Create** | `tests/Majetrack.Features.Tests/Shared/Extensions/ErrorOrEndpointExtensionsTests.cs` | Unit tests — pure in-memory, no DB |

No new project references are needed. `ErrorOr 2.0.1` and `FluentValidation 12.1.1` are already in `Majetrack.Features.csproj`. `IResult` and `Results` come from `Microsoft.AspNetCore.Http` via the existing `FrameworkReference`.

---

## Full API Surface

### 1. `List<Error>.ToHttpResult()` — maps errors to IResult

```csharp
/// <summary>
/// Maps a non-empty list of <see cref="Error"/> values produced by an ErrorOr operation
/// to the appropriate <see cref="IResult"/> HTTP response.
/// The first error's <see cref="ErrorType"/> determines the HTTP status code.
/// When all errors are of type <see cref="ErrorType.Validation"/>, all error descriptions
/// are collected and returned as a RFC 7807 validation problem response.
/// </summary>
/// <param name="errors">
/// The errors from a failed <c>ErrorOr</c> result. Must not be empty.
/// </param>
/// <returns>An <see cref="IResult"/> that produces the appropriate HTTP response.</returns>
public static IResult ToHttpResult(this List<Error> errors)
```

### 2. `ErrorOr<T>.ToHttpResult(onValue)` — convenience overload for endpoint use

```csharp
/// <summary>
/// Maps an <see cref="ErrorOr{TValue}"/> result to an <see cref="IResult"/>.
/// On success, invokes <paramref name="onValue"/> with the result value.
/// On failure, delegates to <see cref="ToHttpResult(List{Error})"/>.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <param name="result">The ErrorOr result to map.</param>
/// <param name="onValue">Factory that produces the success IResult from the value.</param>
/// <returns>An <see cref="IResult"/> for the HTTP response.</returns>
public static IResult ToHttpResult<T>(this ErrorOr<T> result, Func<T, IResult> onValue)
```

### 3. `ValidateRequest<T>` — validate + wrap as ErrorOr

```csharp
/// <summary>
/// Validates <paramref name="request"/> using the provided FluentValidation validator
/// and returns the result as an <see cref="ErrorOr{T}"/>.
/// On validation failure, each <see cref="FluentValidation.Results.ValidationFailure"/>
/// is converted to an <see cref="ErrorType.Validation"/> error whose
/// <see cref="Error.Code"/> is the property name and <see cref="Error.Description"/>
/// is the failure message.
/// On success, the original request is returned as the value.
/// </summary>
/// <typeparam name="T">The request type being validated.</typeparam>
/// <param name="request">The request object to validate.</param>
/// <param name="validator">The FluentValidation validator to run.</param>
/// <param name="ct">Optional cancellation token forwarded to the validator.</param>
/// <returns>
/// <c>ErrorOr.From(request)</c> on success,
/// or a list of <see cref="ErrorType.Validation"/> errors on failure.
/// </returns>
public static async Task<ErrorOr<T>> ValidateRequest<T>(
    this T request,
    IValidator<T> validator,
    CancellationToken ct = default)
```

---

## Error.Type → HTTP Status Mapping

Every `ErrorType` value is handled explicitly. No default fall-through is used — unrecognised future types map to 500 via a final `else` branch.

| `ErrorType` | HTTP Status | `IResult` factory |
|-------------|-------------|-------------------|
| `Validation` (all errors are Validation) | 400 | `Results.ValidationProblem(errors)` |
| `Validation` (mixed — first is Validation) | 400 | `Results.Problem(status: 400)` |
| `NotFound` | 404 | `Results.Problem(status: 404)` |
| `Conflict` | 409 | `Results.Problem(status: 409)` |
| `Unauthorized` | 401 | `Results.Problem(status: 401)` |
| `Forbidden` | 403 | `Results.Problem(status: 403)` |
| `Unexpected` | 500 | `Results.Problem(status: 500)` |
| `Failure` | 500 | `Results.Problem(status: 500)` |
| _(any other / future)_ | 500 | `Results.Problem(status: 500)` |

**Dispatch rule:** inspect `errors[0].Type` (the first error). This is the standard ErrorOr convention — the first error determines the HTTP status. Do not enumerate all errors to find a dominant type.

**Exception:** when *all* errors are `ErrorType.Validation`, use `Results.ValidationProblem()` so the response body includes the full field-keyed error dictionary. When only the first error is `Validation` but others differ (mixed list), fall back to `Results.Problem(status: 400)` with the first error's description.

---

## ValidationProblem Response Shape

`Results.ValidationProblem()` produces RFC 7807 + `application/problem+json` with the following structure:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Amount": ["'Amount' must be greater than 0."],
    "Symbol": ["'Symbol' must not be empty.", "'Symbol' exceeds maximum length."]
  }
}
```

### FluentValidation → ValidationProblem mapping

`ValidateRequest` converts each `ValidationFailure` to:

```csharp
Error.Validation(
    code: failure.PropertyName,   // e.g. "Amount", "Symbol"
    description: failure.ErrorMessage)
```

`ToHttpResult` then converts these back to the dictionary expected by `Results.ValidationProblem()`:

```csharp
var dict = errors
    .GroupBy(e => e.Code)
    .ToDictionary(
        g => g.Key,
        g => g.Select(e => e.Description).ToArray());

return Results.ValidationProblem(dict);
```

This preserves multiple errors per field (FluentValidation can produce several failures for a single property).

**Property name format:** FluentValidation emits the C# property name by default (`"Amount"`, not `"amount"`). Do not force snake_case — the convention is that consumers read the property names as-is. If JSON serialisation uses camelCase, the error keys will still be PascalCase unless overridden in FluentValidation config; leave this as-is for now.

---

## Match / IsError Pattern

The `ErrorOr<T>.ToHttpResult(onValue)` overload uses `Match`:

```csharp
public static IResult ToHttpResult<T>(this ErrorOr<T> result, Func<T, IResult> onValue)
    => result.Match(
        value  => onValue(value),
        errors => errors.ToHttpResult());
```

`ErrorOr<T>.Match(onValue, onError)` is the v2 API — it accepts two `Func` delegates and returns their common type. No async variant is needed here because both branches return `IResult` synchronously; the calling endpoint handler is `async` and `await`s the overall operation chain, not this mapping step.

Endpoints that need custom success shapes (e.g. `Results.Created`) pass a lambda:

```csharp
return result.ToHttpResult(value => Results.Created($"/api/transactions/{value.Id}", value));
```

---

## File Structure (single file decision)

Both extensions live in the same file. Rationale:

- `ValidateRequest` produces `ErrorOr<T>`.
- `ToHttpResult` consumes `ErrorOr<T>`.
- They are always used together; splitting them adds navigation friction with no benefit.
- The file stays small (< 80 lines of implementation).

```
src/Majetrack.Features/
  Shared/
    Extensions/
      ErrorOrEndpointExtensions.cs   ← both extensions
```

Namespace: `Majetrack.Features.Shared.Extensions`

---

## Edge Cases

### 1. Empty error list
`ErrorOr` guarantees a non-empty error list on failure — `ErrorOr<T>` cannot be constructed with zero errors. However, if `List<Error>` is called with an empty list (e.g. from a bug in calling code), return `Results.Problem(status: 500)` with a description of `"Unknown error"`. Assert defensively rather than throwing, since the extension is called in a hot path.

```csharp
if (errors.Count == 0)
    return Results.Problem(statusCode: 500, detail: "An unknown error occurred.");
```

### 2. Multiple errors of different types
Only the first error's type is inspected. This is the documented ErrorOr v2 convention (`FirstError` property). Mixed-type lists with Validation-first errors return `Results.Problem(status: 400)` rather than `Results.ValidationProblem()`, because building a validation dict from non-Validation errors is incorrect.

### 3. All errors are Validation (most common case)
The all-Validation shortcut: if `errors.All(e => e.Type == ErrorType.Validation)` → use `ValidationProblem`. This is checked before the first-error dispatch.

### 4. ValidateRequest with a null validator
Not guarded — callers must inject a valid `IValidator<T>`. Passing `null` results in a `NullReferenceException`, which the GlobalExceptionHandler (T-09) will catch and convert to 500. No defensive null check needed.

### 5. Cancellation during validation
`validator.ValidateAsync(request, ct)` respects the `CancellationToken`. If cancelled, `FluentValidation` throws `OperationCanceledException`, which propagates out of `ValidateRequest` to the endpoint handler. The GlobalExceptionHandler converts this to 499. This is correct behaviour — no special handling needed here.

---

## Implementation Sketch

```csharp
namespace Majetrack.Features.Shared.Extensions;

/// <summary>
/// Extension methods for mapping ErrorOr results to ASP.NET Core IResult HTTP responses,
/// and for validating request objects using FluentValidation within the ErrorOr pipeline.
/// </summary>
public static class ErrorOrEndpointExtensions
{
    /// <summary>...</summary>
    public static IResult ToHttpResult(this List<Error> errors)
    {
        if (errors.Count == 0)
            return Results.Problem(statusCode: 500, detail: "An unknown error occurred.");

        // All-Validation shortcut → structured ValidationProblem with field dictionary
        if (errors.All(e => e.Type == ErrorType.Validation))
        {
            var dict = errors
                .GroupBy(e => e.Code)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Description).ToArray());
            return Results.ValidationProblem(dict);
        }

        // First-error dispatch for all other types
        return errors[0].Type switch
        {
            ErrorType.Validation   => Results.Problem(statusCode: 400, detail: errors[0].Description),
            ErrorType.NotFound     => Results.Problem(statusCode: 404, detail: errors[0].Description),
            ErrorType.Conflict     => Results.Problem(statusCode: 409, detail: errors[0].Description),
            ErrorType.Unauthorized => Results.Problem(statusCode: 401, detail: errors[0].Description),
            ErrorType.Forbidden    => Results.Problem(statusCode: 403, detail: errors[0].Description),
            _                      => Results.Problem(statusCode: 500, detail: errors[0].Description),
        };
    }

    /// <summary>...</summary>
    public static IResult ToHttpResult<T>(this ErrorOr<T> result, Func<T, IResult> onValue)
        => result.Match(
            value  => onValue(value),
            errors => errors.ToHttpResult());

    /// <summary>...</summary>
    public static async Task<ErrorOr<T>> ValidateRequest<T>(
        this T request,
        IValidator<T> validator,
        CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (validation.IsValid)
            return request;

        return validation.Errors
            .ConvertAll(f => Error.Validation(f.PropertyName, f.ErrorMessage));
    }
}
```

---

## Test Strategy

Unit tests only — no HTTP pipeline needed. All branches are pure transformations of in-memory objects.

### Test file
`tests/Majetrack.Features.Tests/Shared/Extensions/ErrorOrEndpointExtensionsTests.cs`

### Coverage required by acceptance criteria

| Scenario | Expected outcome |
|----------|-----------------|
| Single Validation error | `Results.ValidationProblem`, 400, field key present |
| Multiple Validation errors, same field | field key has array with both messages |
| Multiple Validation errors, different fields | both field keys present |
| Mixed errors (Validation first) | `Results.Problem(400)` — not ValidationProblem |
| NotFound error | `Results.Problem(404)` |
| Conflict error | `Results.Problem(409)` |
| Unauthorized error | `Results.Problem(401)` |
| Unexpected error | `Results.Problem(500)` |
| Failure error | `Results.Problem(500)` |
| Empty error list | `Results.Problem(500)` with detail |
| `ToHttpResult<T>` — success path | `onValue` invoked, result returned |
| `ToHttpResult<T>` — error path | delegates to `List<Error>.ToHttpResult()` |
| `ValidateRequest` — valid request | returns `ErrorOr<T>` with value |
| `ValidateRequest` — invalid request | returns errors with Validation type |
| `ValidateRequest` — multiple failures | all failures present as Validation errors |
| `ValidateRequest` — failure code = property name | `Error.Code` matches `PropertyName` |
| `ValidateRequest` — failure description = message | `Error.Description` matches `ErrorMessage` |

Tests use `FluentAssertions`. The `IResult` returned by `Results.ValidationProblem()` and `Results.Problem()` can be inspected by executing the result against a mock `HttpContext` (using `Microsoft.AspNetCore.Http.DefaultHttpContext` — no web server needed), or by checking the concrete type (`Microsoft.AspNetCore.Http.HttpResults.ValidationProblem`, `Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult`).

Preferred approach: cast to the known concrete type to avoid spinning up a full HttpContext:

```csharp
var result = errors.ToHttpResult();
result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
      .Which.ProblemDetails.Status.Should().Be(404);
```

For `ValidationProblem`:
```csharp
result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>()
      .Which.ProblemDetails.Errors.Should().ContainKey("Amount");
```

---

## Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Single file vs. split | Single file | The two helpers are always co-used; splitting adds no value |
| First-error dispatch | `errors[0].Type` | Standard ErrorOr v2 convention; consistent with `result.FirstError` |
| All-Validation shortcut | `errors.All(e => e.Type == ErrorType.Validation)` | Only correct case for `ValidationProblem` field dictionary; mixed lists must not use it |
| Property name as Error.Code | `failure.PropertyName` | Preserves grouping capability; avoids information loss |
| `Match` over `IsError` | `result.Match(...)` | Functional, avoids accessing `.Value` unsafely |
| Test type | Unit only | Pure mapping logic; no I/O, no pipeline |
| Concrete result type assertions | Cast to `ProblemHttpResult` / `ValidationProblem` | Avoids spinning up test server; deterministic and fast |

---

## Open Questions

None — all mapping behaviour is fully specified by the acceptance criteria and ErrorOr v2 semantics.

---

_Architect: claude-sonnet-4-6 | Date: 2026-04-03 | Task: T-10_
