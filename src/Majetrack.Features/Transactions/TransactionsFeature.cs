using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features.Transactions;

/// <summary>
/// Configures the Transactions feature module.
/// Registers transaction-specific services and maps the <c>/api/transactions</c> route group
/// that will host all transaction-related endpoints (create, list, import, etc.).
/// </summary>
public class TransactionsFeature : IFeatureConfiguration
{
    /// <inheritdoc />
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Transaction-specific services will be registered here as endpoints are added.
    }

    /// <inheritdoc />
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/transactions")
           .WithTags("Transactions");
    }
}
