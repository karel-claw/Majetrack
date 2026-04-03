namespace Majetrack.Domain.Entities;

/// <summary>
/// Represents an authenticated user of the portfolio tracking application.
/// Stores identity and login metadata linked to Microsoft Entra ID.
/// </summary>
public class User
{
    /// <summary>
    /// Unique internal identifier for the user, generated at registration time.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The object ID from Microsoft Entra ID (Azure AD) used to correlate the local user
    /// record with the external identity provider.
    /// </summary>
    public string EntraObjectId { get; set; } = string.Empty;

    /// <summary>
    /// The user's email address, sourced from the identity provider claims.
    /// Used for display and notification purposes.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name for the user, typically sourced from the identity provider.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp indicating when the user record was first created in the system.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of the user's most recent login.
    /// Null if the user has never logged in after initial registration.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }
}
