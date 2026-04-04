using FluentValidation;
using Majetrack.Features.Transactions.Create;
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
        services.AddScoped<IValidator<CreateTransactionRequest>, CreateTransactionValidator>();
        services.AddScoped<CreateTransactionFeature>();
    }

    /// <inheritdoc />
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions")
                       .WithTags("Transactions");

        group.MapPost("", CreateTransactionEndpoint.HandleAsync)
             .RequireAuthorization();
    }
}
