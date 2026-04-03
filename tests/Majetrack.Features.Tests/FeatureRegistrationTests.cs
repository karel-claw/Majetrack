using System.Reflection;
using FluentAssertions;
using Majetrack.Features;
using Majetrack.Features.Portfolio;
using Majetrack.Features.Transactions;
using Majetrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Majetrack.Features.Tests;

/// <summary>
/// Verifies that the feature registration pipeline correctly discovers
/// and wires up all <see cref="IFeatureConfiguration"/> implementations.
/// </summary>
public class FeatureRegistrationTests : IClassFixture<FeatureRegistrationTests.TestFactory>
{
    private readonly TestFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureRegistrationTests"/> class.
    /// </summary>
    /// <param name="factory">The web application factory provided by xUnit fixture.</param>
    public FeatureRegistrationTests(TestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Assembly scanning should discover both TransactionsFeature and PortfolioFeature.
    /// </summary>
    [Fact]
    public void AssemblyScanning_FindsAllFeatureConfigurations()
    {
        var assembly = typeof(IFeatureConfiguration).Assembly;

        var featureTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && t.GetInterfaces().Contains(typeof(IFeatureConfiguration)))
            .ToList();

        featureTypes.Should().Contain(t => t == typeof(TransactionsFeature));
        featureTypes.Should().Contain(t => t == typeof(PortfolioFeature));
    }

    /// <summary>
    /// AddFeatures should not throw when invoked during startup.
    /// </summary>
    [Fact]
    public void AddFeatures_DoesNotThrow()
    {
        var act = () => _factory.CreateClient();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The transactions route group should be registered and return a non-500 status code.
    /// </summary>
    [Fact]
    public async Task GetTransactions_ReturnsNon500StatusCode()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/transactions");

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    /// <summary>
    /// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces the PostgreSQL
    /// database with an in-memory provider and uses the Testing environment to skip migrations.
    /// </summary>
    public class TestFactory : WebApplicationFactory<Program>
    {
        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<MajetrackDbContext>));

                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<MajetrackDbContext>(options =>
                    options.UseInMemoryDatabase("TestDb"));
            });
        }
    }
}
