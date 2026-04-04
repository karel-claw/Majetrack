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
            // Entra ID / Azure AD uses "oid" (object id) as the stable unique user claim.
            // Fall back to the standard NameIdentifier claim for flexibility.
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
