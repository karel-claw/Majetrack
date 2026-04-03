using Majetrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Majetrack.Features.Assets.List;

/// <summary>
/// Handles GET /api/assets requests by querying the asset catalogue with optional filters.
/// Returns a list of assets ordered alphabetically by name.
/// </summary>
internal static class GetAssetsHandler
{
    /// <summary>
    /// Retrieves the list of assets from the database, optionally filtered by platform and/or asset type.
    /// Results are ordered alphabetically by name and projected to the response DTO.
    /// </summary>
    /// <param name="filter">Optional query parameters for filtering by platform and asset type.</param>
    /// <param name="db">The database context for querying assets.</param>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>HTTP 200 with a JSON array of assets (may be empty if no matches).</returns>
    /// <remarks>
    /// TODO T-07: When Entra ID JWT bearer auth is configured, remove .AllowAnonymous()
    ///            and add .RequireAuthorization() to the AssetsFeature route group.
    /// TODO T-08: ICurrentUser service will provide HttpContext.User claims extraction.
    ///            For GET /api/assets, ICurrentUser is not used for filtering
    ///            but may be injected for audit logging in the future.
    /// </remarks>
    internal static async Task<IResult> HandleAsync(
        [AsParameters] AssetListQuery filter,
        MajetrackDbContext db,
        CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking();

        if (filter.Platform is { } platform)
        {
            query = query.Where(a => a.Platform == platform);
        }

        if (filter.AssetType is { } assetType)
        {
            query = query.Where(a => a.AssetType == assetType);
        }

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

        return Results.Ok(assets);
    }
}
