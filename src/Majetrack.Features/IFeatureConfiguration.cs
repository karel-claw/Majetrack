using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features;

/// <summary>
/// Defines the contract for a vertical-slice feature module.
/// Each feature implements this interface to register its own services and map its endpoints,
/// enabling automatic discovery and registration via assembly scanning.
/// </summary>
public interface IFeatureConfiguration
{
    /// <summary>
    /// Registers services required by this feature into the dependency injection container.
    /// Called during application startup by the feature registration pipeline.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The application configuration for reading feature-specific settings.</param>
    static abstract void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps the HTTP endpoints for this feature onto the application's routing pipeline.
    /// Called after the application is built but before it starts accepting requests.
    /// </summary>
    /// <param name="app">The endpoint route builder to map routes on.</param>
    static abstract void MapEndpoints(IEndpointRouteBuilder app);
}
