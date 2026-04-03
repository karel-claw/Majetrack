using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features;

/// <summary>
/// Provides extension methods that scan an assembly for <see cref="IFeatureConfiguration"/>
/// implementations and invoke their registration methods automatically.
/// This enables a plug-in style architecture where each feature module is self-contained.
/// </summary>
public static class FeatureRegistrationExtensions
{
    /// <summary>
    /// Scans the specified assembly for all concrete types implementing <see cref="IFeatureConfiguration"/>
    /// and invokes their <c>AddServices</c> methods to register feature-specific services.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">The application configuration passed to each feature.</param>
    /// <param name="assembly">The assembly to scan for feature configurations.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddFeatures(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly assembly)
    {
        var featureTypes = GetFeatureTypes(assembly);

        foreach (var type in featureTypes)
        {
            var method = type.GetMethod(
                nameof(IFeatureConfiguration.AddServices),
                BindingFlags.Public | BindingFlags.Static);

            method?.Invoke(null, [services, configuration]);
        }

        return services;
    }

    /// <summary>
    /// Scans the specified assembly for all concrete types implementing <see cref="IFeatureConfiguration"/>
    /// and invokes their <c>MapEndpoints</c> methods to register feature-specific routes.
    /// </summary>
    /// <param name="app">The endpoint route builder to map routes on.</param>
    /// <param name="assembly">The assembly to scan for feature configurations.</param>
    /// <returns>The same <see cref="IEndpointRouteBuilder"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapFeatures(
        this IEndpointRouteBuilder app,
        Assembly assembly)
    {
        var featureTypes = GetFeatureTypes(assembly);

        foreach (var type in featureTypes)
        {
            var method = type.GetMethod(
                nameof(IFeatureConfiguration.MapEndpoints),
                BindingFlags.Public | BindingFlags.Static);

            method?.Invoke(null, [app]);
        }

        return app;
    }

    /// <summary>
    /// Returns all concrete, non-abstract types in the given assembly
    /// that implement <see cref="IFeatureConfiguration"/>.
    /// </summary>
    private static IEnumerable<Type> GetFeatureTypes(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && t.GetInterfaces().Contains(typeof(IFeatureConfiguration)));
    }
}
