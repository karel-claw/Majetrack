using Majetrack.Features.Shared.Services;
using System.Security.Claims;

namespace Majetrack.Api.Infrastructure;

/// <summary>
/// Resolves the current user's identity from the HTTP context's
/// <see cref="ClaimsPrincipal"/> (JWT bearer token claims).
/// Registered as a scoped service so each request gets a fresh snapshot.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="HttpCurrentUser"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated =>
        User?.Identity?.IsAuthenticated is true;

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            // Prefer the internal DB User.Id stored by EnsureUserMiddleware.
            // This avoids confusing the Entra OID (external identity) with
            // the application's own primary key.
            var items = _httpContextAccessor.HttpContext?.Items;
            if (items is not null
                && items.TryGetValue(EnsureUserMiddleware.InternalUserIdKey, out var raw)
                && raw is Guid internalId)
            {
                return internalId;
            }

            // Fallback: should not be reached for authenticated requests that
            // passed through EnsureUserMiddleware, but kept for safety.
            var value = User?.FindFirstValue("oid")
                     ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public string? Email =>
        User?.FindFirstValue("preferred_username")
     ?? User?.FindFirstValue(ClaimTypes.Email);
}
