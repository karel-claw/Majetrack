using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features.Portfolio;

/// <summary>
/// Configures the Portfolio feature module.
/// Registers portfolio-specific services and maps the <c>/api/portfolio</c> route group
/// that will host all portfolio-related endpoints (summary, positions, etc.).
/// </summary>
public class PortfolioFeature : IFeatureConfiguration
{
    /// <inheritdoc />
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Portfolio-specific services will be registered here as endpoints are added.
    }

    /// <inheritdoc />
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/portfolio")
           .WithTags("Portfolio");
    }
}
