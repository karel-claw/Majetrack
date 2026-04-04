using Majetrack.Features.Shared.Extensions;
using Microsoft.AspNetCore.Http;

namespace Majetrack.Features.Transactions.Create;

/// <summary>
/// Minimal API handler for POST /api/transactions.
/// Delegates to <see cref="CreateTransactionFeature"/> and maps the result to HTTP responses.
/// </summary>
public static class CreateTransactionEndpoint
{
    /// <summary>
    /// Handles the POST /api/transactions request.
    /// Returns 201 Created with a Location header on success, or the appropriate error response.
    /// </summary>
    /// <param name="request">The JSON request body.</param>
    /// <param name="feature">The create transaction feature, resolved from DI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IResult"/> representing the HTTP response.</returns>
    public static async Task<IResult> HandleAsync(
        CreateTransactionRequest request,
        CreateTransactionFeature feature,
        CancellationToken ct)
    {
        var result = await feature.ExecuteAsync(request, ct);

        return result.ToHttpResult(id =>
            Results.Created($"/api/transactions/{id}", new { id }));
    }
}
