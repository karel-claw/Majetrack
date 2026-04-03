# T-11 — GET /api/assets — Design Document

_Status: Draft | Author: architect agent | Date: 2026-04-03_

---

## 1. Vertical Slice — Folder Structure

```
src/Majetrack.Features/
└── Assets/
    ├── AssetsFeature.cs          ← IFeatureConfiguration (registers route group, no extra services)
    └── List/
        ├── AssetListQuery.cs     ← Query params record ([AsParameters] binding)
        ├── AssetResponse.cs      ← Response DTO record
        └── GetAssetsHandler.cs   ← Static handler method, EF Core query, ErrorOr result
```

```
tests/Majetrack.Features.Tests/
└── Assets/
    └── GetAssetsTests.cs         ← Integration tests (happy path + filter combinations + 401 placeholder)
```

No new infrastructure files are needed. No migration is needed — `DbSet<Asset>` already exists.

---

## 2. Request / Response Shape

### Query Parameters

| Parameter   | Type          | Required | Notes                                     |
|-------------|---------------|----------|-------------------------------------------|
| `platform`  | `Platform?`   | No       | Enum: `Xtb`, `Etoro`, `Investown`        |
| `assetType` | `AssetType?`  | No       | Enum: `Stock`, `Etf`, `P2pLoan`          |

ASP.NET Core binds query strings to nullable enums by name (case-insensitive) via `[AsParameters]`.
Malformed enum values → ASP.NET framework-level 400 (no custom validation needed).

```csharp
// AssetListQuery.cs
namespace Majetrack.Features.Assets.List;

/// <summary>
/// Query parameters for filtering the asset list.
/// Both filters are optional; omitting them returns all assets.
/// </summary>
public record AssetListQuery
{
    /// <summary>The trading platform to filter by. Null means no filter.</summary>
    public Platform? Platform { get; init; }

    /// <summary>The asset type to filter by. Null means no filter.</summary>
    public AssetType? AssetType { get; init; }
}
```

### Response DTO

```csharp
// AssetResponse.cs
namespace Majetrack.Features.Assets.List;

/// <summary>
/// Represents a single asset entry returned by GET /api/assets.
/// </summary>
public record AssetResponse(
    Guid Id,
    string? Ticker,
    string Name,
    string AssetType,
    string? Exchange,
    string Currency,
    string Platform
);
```

Enums are serialized as **strings** (PascalCase) to match how EF Core stores them and to keep the API human-readable. No integer leakage.

### Response Envelope

```
HTTP 200 OK
Content-Type: application/json

[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "ticker": "AAPL",
    "name": "Apple Inc.",
    "assetType": "Stock",
    "exchange": "NASDAQ",
    "currency": "Usd",
    "platform": "Xtb"
  }
]
```

Empty filter result → `200 []` (not 404). This is a list resource; absence of items is valid.

---

## 3. EF Core Query Strategy

### Approach: Conditional LINQ filtering on `IQueryable<Asset>`

```csharp
IQueryable<Asset> query = db.Assets.AsNoTracking();

if (filter.Platform is not null)
    query = query.Where(a => a.Platform == filter.Platform.Value);

if (filter.AssetType is not null)
    query = query.Where(a => a.AssetType == filter.AssetType.Value);

var assets = await query
    .OrderBy(a => a.Name)
    .Select(a => new AssetResponse(
        a.Id,
        a.Ticker,
        a.Name,
        a.AssetType.ToString(),
        a.Exchange,
        a.Currency.ToString(),
        a.Platform.ToString()))
    .ToListAsync(ct);
```

**Why this approach:**
- Single SQL query regardless of filter combination — no N+1, no post-query filtering.
- `AsNoTracking()` — read-only path, no change tracking overhead.
- Server-side `.Select()` projection — only the columns needed are fetched; no materializing the full `Asset` entity.
- Deterministic ordering by `Name` keeps response stable for clients.
- EF Core translates nullable enum comparisons correctly to PostgreSQL string comparisons (enums stored as strings via `HasConversion<string>()`).

**Index coverage:**
No composite index covers `(Platform, AssetType)` on the `Assets` table.
Given the expected data volume (hundreds of assets, not millions), a sequential scan is acceptable.
If performance becomes an issue post-MVP, add: `CREATE INDEX ix_assets_platform_asset_type ON assets (platform, asset_type)`.

**No pagination in scope for T-11.** The asset list is bounded by the number of supported instruments — no cursor/offset needed at this stage.

---

## 4. ErrorOr Chain Design

### Can this endpoint fail at the application level?

| Scenario                           | How it surfaces                                                   |
|------------------------------------|-------------------------------------------------------------------|
| DB unreachable                     | Exception → `GlobalExceptionHandler` → 500 ProblemDetails        |
| Malformed enum in query string     | ASP.NET model binding failure → framework 400 (before handler)   |
| Valid request, no matching assets  | `200 []` — not an error                                           |

**Conclusion:** There are no application-level failure paths that require `ErrorOr` for _this_ endpoint. The handler always succeeds or throws.

Using `ErrorOr<List<AssetResponse>>` here would add noise without value. The handler returns `IResult` directly:

```csharp
// GetAssetsHandler.cs (simplified)
internal static async Task<IResult> HandleAsync(
    [AsParameters] AssetListQuery filter,
    MajetrackDbContext db,
    CancellationToken ct)
{
    // TODO T-07/T-08: replace [AllowAnonymous] with .RequireAuthorization() on the route group.
    // When auth is wired, inject ICurrentUser here for audit logging if needed.
    // This endpoint does NOT filter by user — it returns the shared asset catalogue.

    IQueryable<Asset> query = db.Assets.AsNoTracking();

    if (filter.Platform is not null)
        query = query.Where(a => a.Platform == filter.Platform.Value);

    if (filter.AssetType is not null)
        query = query.Where(a => a.AssetType == filter.AssetType.Value);

    var assets = await query
        .OrderBy(a => a.Name)
        .Select(a => new AssetResponse(
            a.Id, a.Ticker, a.Name,
            a.AssetType.ToString(), a.Exchange,
            a.Currency.ToString(), a.Platform.ToString()))
        .ToListAsync(ct);

    return Results.Ok(assets);
}
```

If future iterations add business rules (e.g., visibility toggles, tenant scoping), introduce `ErrorOr<List<AssetResponse>>` at that point via `ValidateRequest` + `ToHttpResult`.

---

## 5. ICurrentUser — Auth Wiring Placeholder (T-07 / T-08)

### Current state (T-11 scope)
Auth middleware is registered (`AddAuthentication` / `AddAuthorization`) but no scheme or policy is configured. The endpoint is marked `[AllowAnonymous]` to prevent a 401 from the framework before any business logic runs.

A code comment in the handler signals the intent:
```csharp
// TODO T-07: When Entra ID JWT bearer auth is configured, remove [AllowAnonymous]
//            and add .RequireAuthorization() to the AssetsFeature route group.
// TODO T-08: ICurrentUser service will provide HttpContext.User claims extraction.
//            For GET /api/assets, ICurrentUser is not used for filtering
//            but may be injected for audit logging in the future.
```

### How auth will be wired (T-07 / T-08 forecast)

```csharp
// AssetsFeature.cs — after T-07
public static void MapEndpoints(IEndpointRouteBuilder app)
{
    app.MapGroup("/api/assets")
       .WithTags("Assets")
       .RequireAuthorization()           // ← added in T-07
       .MapGet("", GetAssetsHandler.HandleAsync);
}
```

`ICurrentUser` (to be defined in T-08) will extract the user's `EntraObjectId` from `HttpContext.User` claims. For this endpoint it is injected but unused — it serves as the auth gate only.

---

## 6. IFeatureConfiguration Registration

```csharp
// AssetsFeature.cs
namespace Majetrack.Features.Assets;

/// <summary>
/// Configures the Assets feature: HTTP routing and DI services.
/// </summary>
public class AssetsFeature : IFeatureConfiguration
{
    /// <summary>
    /// No additional services required for the Assets read feature.
    /// </summary>
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // No feature-specific services at this stage.
    }

    /// <summary>
    /// Maps GET /api/assets with optional platform and assetType filters.
    /// </summary>
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/assets")
           .WithTags("Assets")
           // TODO T-07: .RequireAuthorization()
           .MapGet("", GetAssetsHandler.HandleAsync);
    }
}
```

`FeatureRegistrationExtensions` discovers `AssetsFeature` automatically via assembly scanning — no manual registration in `Program.cs`.

---

## 7. NuGet Packages Needed

None. All required packages are already present:

| Package                       | Already in project? | Used for                        |
|-------------------------------|---------------------|---------------------------------|
| `Microsoft.EntityFrameworkCore` | ✅ Yes            | `IQueryable`, `AsNoTracking`    |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | ✅ Yes  | PostgreSQL provider             |
| `ErrorOr`                     | ✅ Yes              | (not used in this handler)      |
| `FluentValidation`            | ✅ Yes              | (not used in this handler)      |

---

## 8. Open Questions

| # | Question | Impact | Suggested Resolution |
|---|----------|--------|----------------------|
| 1 | Should enum query params accept integer values (e.g., `?platform=1`) or string only? | API usability | Default ASP.NET binding accepts both. Document string-only in OpenAPI to discourage int coupling. |
| 2 | Should the response include `createdAt`? The GitHub issue omits it but the entity has it. | API contract | Omit for now — keep response minimal; add later if needed by UI. |
| 3 | Ordering: alphabetical by `name` assumed. Is this correct? | UX | Confirm with product. Default: `ORDER BY name ASC`. |
| 4 | When T-07 lands, will `RequireAuthorization` use a default policy or a named one? | Auth design | Depends on T-07 implementation. The TODO comment in `MapEndpoints` is the handoff point. |
| 5 | Is there a need to filter by `currency` in addition to `platform` and `assetType`? | Scope | Not in T-11 AC. Out of scope unless requested. |

---

_End of design document._
