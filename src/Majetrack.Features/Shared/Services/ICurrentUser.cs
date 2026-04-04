namespace Majetrack.Features.Shared.Services;

/// <summary>
/// Provides access to the currently authenticated user's identity.
/// Implementations extract user claims from the HTTP context's JWT bearer token.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the unique identifier of the currently authenticated user.
    /// Returns <c>null</c> when the request is unauthenticated.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Gets the email address of the currently authenticated user.
    /// Returns <c>null</c> when the request is unauthenticated or the claim is absent.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
