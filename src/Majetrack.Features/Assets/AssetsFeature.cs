using Majetrack.Features.Assets.List;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features.Assets;

/// <summary>
/// Configures the Assets feature module.
/// Registers asset-specific services and maps the <c>/api/assets</c> route group
/// that hosts endpoints for querying the shared asset catalogue.
/// </summary>
public class AssetsFeature : IFeatureConfiguration
{
    /// <summary>
    /// Registers services required by the Assets feature.
    /// Currently no additional services are needed for the read-only asset catalogue.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The application configuration for reading feature-specific settings.</param>
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // No feature-specific services required for the Assets read feature at this stage.
    }

    /// <summary>
    /// Maps the HTTP endpoints for the Assets feature.
    /// Currently exposes GET /api/assets for retrieving the asset catalogue.
    /// </summary>
    /// <param name="app">The endpoint route builder to map routes on.</param>
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets")
                       .WithTags("Assets");

        group.MapGet("", GetAssetsHandler.HandleAsync)
             .RequireAuthorization();
    }
}
