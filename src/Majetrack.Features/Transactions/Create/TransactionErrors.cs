using ErrorOr;

namespace Majetrack.Features.Transactions.Create;

/// <summary>
/// Defines domain-specific errors for the Create Transaction feature.
/// Each error maps to an appropriate HTTP status code via the ErrorOr pipeline.
/// </summary>
public static class TransactionErrors
{
    /// <summary>
    /// The specified asset was not found, or the asset belongs to a different user.
    /// Returns 404 to avoid leaking information about assets owned by other users.
    /// </summary>
    public static Error AssetNotFound => Error.NotFound(
        "Transaction.AssetNotFound",
        "The specified asset was not found.");

    /// <summary>
    /// The request was made without valid authentication credentials.
    /// </summary>
    public static Error Unauthenticated => Error.Unauthorized(
        "Transaction.Unauthenticated",
        "Authentication is required to create a transaction.");

    /// <summary>
    /// The foreign exchange rate service is temporarily unavailable.
    /// Returns 502 Bad Gateway.
    /// </summary>
    public static Error FxRateUnavailable => Error.Unexpected(
        "Transaction.FxRateUnavailable",
        "The exchange rate service is temporarily unavailable. Please try again later.");
}
