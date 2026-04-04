using Majetrack.Domain.Entities;
using Majetrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Majetrack.Api.Infrastructure;

/// <summary>
/// Middleware that ensures a local <see cref="User"/> record exists for every authenticated request.
/// When a valid Entra ID JWT is present and no matching user is found in the database,
/// a new user is created using claims from the token.
/// </summary>
/// <remarks>
/// Must be placed after <c>UseAuthentication()</c> and <c>UseAuthorization()</c>
/// in the middleware pipeline so that <see cref="HttpContext.User"/> is fully populated.
///
/// Entra ID claim mapping:
/// <list type="bullet">
///   <item><term>oid</term><description>Entra Object ID → <see cref="User.EntraObjectId"/></description></item>
///   <item><term>preferred_username / upn / email</term><description>→ <see cref="User.Email"/></description></item>
///   <item><term>name</term><description>Display name → <see cref="User.DisplayName"/></description></item>
/// </list>
/// </remarks>
public class EnsureUserMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EnsureUserMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EnsureUserMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public EnsureUserMiddleware(RequestDelegate next, ILogger<EnsureUserMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware. If the request is authenticated, ensures a local
    /// user record exists for the Entra identity, creating one if necessary.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="db">The database context (scoped per request via DI).</param>
    public async Task InvokeAsync(HttpContext context, MajetrackDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var entraObjectId = GetEntraObjectId(context.User);

            if (entraObjectId is null)
            {
                _logger.LogWarning(
                    "Authenticated request is missing 'oid' claim. TraceId={TraceId}",
                    context.TraceIdentifier);
            }
            else
            {
                await EnsureUserExistsAsync(db, entraObjectId, context.User, context.RequestAborted);
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Checks whether the user exists in the database and creates a new record if not.
    /// Updates <see cref="User.LastLoginAt"/> on every authenticated request.
    /// </summary>
    private async Task EnsureUserExistsAsync(
        MajetrackDbContext db,
        string entraObjectId,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                EntraObjectId = entraObjectId,
                Email = GetEmail(principal),
                DisplayName = GetDisplayName(principal),
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow,
            };

            db.Users.Add(user);

            _logger.LogInformation(
                "Created new user from Entra token. UserId={UserId} EntraObjectId={EntraObjectId} Email={Email}",
                user.Id,
                user.EntraObjectId,
                user.Email);
        }
        else
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Claim extraction helpers ─────────────────────────────────────────────

    /// <summary>
    /// Extracts the Entra Object ID from the <c>oid</c> claim.
    /// Returns <c>null</c> if the claim is absent.
    /// </summary>
    private static string? GetEntraObjectId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("oid")
        ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

    /// <summary>
    /// Extracts the user's email address, trying multiple standard Entra claim names in order.
    /// Falls back to an empty string if none are present.
    /// </summary>
    private static string GetEmail(ClaimsPrincipal principal) =>
        principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue("upn")
        ?? principal.FindFirstValue(ClaimTypes.Upn)
        ?? principal.FindFirstValue(ClaimTypes.Email)
        ?? string.Empty;

    /// <summary>
    /// Extracts the user's display name from the <c>name</c> claim.
    /// Falls back to <c>email</c> if <c>name</c> is absent.
    /// </summary>
    private static string GetDisplayName(ClaimsPrincipal principal) =>
        principal.FindFirstValue("name")
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? GetEmail(principal);
}
