using Majetrack.Features.Shared.Services;

namespace Majetrack.Features.Tests.Shared;

/// <summary>
/// A simple, settable implementation of <see cref="ICurrentUser"/> for unit tests.
/// Replaces ad-hoc <c>Mock&lt;ICurrentUser&gt;</c> setup with a typed, readable helper.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    /// <summary>
    /// Initializes an authenticated test user with the given <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">The user ID to return from <see cref="UserId"/>.</param>
    /// <param name="email">Optional email address. Defaults to a generated address.</param>
    public TestCurrentUser(Guid userId, string? email = null)
    {
        UserId = userId;
        Email = email ?? $"user-{userId:N}@test.example";
        IsAuthenticated = true;
    }

    /// <summary>
    /// Initializes an unauthenticated test user (all properties return null/false).
    /// </summary>
    public TestCurrentUser()
    {
        UserId = null;
        Email = null;
        IsAuthenticated = false;
    }

    /// <inheritdoc />
    public Guid? UserId { get; }

    /// <inheritdoc />
    public string? Email { get; }

    /// <inheritdoc />
    public bool IsAuthenticated { get; }

    /// <summary>
    /// Returns an authenticated <see cref="TestCurrentUser"/> for the given <paramref name="userId"/>.
    /// </summary>
    public static TestCurrentUser Authenticated(Guid userId, string? email = null)
        => new(userId, email);

    /// <summary>
    /// Returns an unauthenticated <see cref="TestCurrentUser"/>.
    /// </summary>
    public static TestCurrentUser Unauthenticated()
        => new();
}
