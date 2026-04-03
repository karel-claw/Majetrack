using System.Net;
using System.Text.Json;
using FluentAssertions;
using Majetrack.Domain.Entities;
using Majetrack.Domain.Enums;
using Majetrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Majetrack.Features.Tests.Assets;

/// <summary>
/// Integration tests for GET /api/assets endpoint.
/// Verifies filtering by platform and asset type, response shape, and edge cases.
/// </summary>
public class GetAssetsTests : IClassFixture<GetAssetsTests.TestFactory>, IAsyncLifetime
{
    private readonly TestFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetAssetsTests"/> class.
    /// </summary>
    /// <param name="factory">The web application factory provided by xUnit fixture.</param>
    public GetAssetsTests(TestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    #region Group 1 - Happy Path (no filters)

    /// <summary>
    /// TC-01: Returns HTTP 200 when no filters are applied.
    /// </summary>
    [Fact]
    public async Task TC01_NoFilters_ReturnsHttp200()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// TC-02: Content-Type is application/json.
    /// </summary>
    [Fact]
    public async Task TC02_NoFilters_ReturnsApplicationJson()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().StartWith("application/json");
    }

    /// <summary>
    /// TC-03: Returns all 5 seeded assets when no filters are applied.
    /// </summary>
    [Fact]
    public async Task TC03_NoFilters_ReturnsAll5Assets()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert
        assets.Should().HaveCount(5);
    }

    /// <summary>
    /// TC-04: Each item contains all required fields with correct types.
    /// </summary>
    [Fact]
    public async Task TC04_ResponseItems_ContainAllRequiredFields()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert
        var firstAsset = assets.First();
        firstAsset.Id.Should().NotBe(Guid.Empty);
        firstAsset.Name.Should().NotBeNullOrEmpty();
        firstAsset.AssetType.Should().NotBeNullOrEmpty();
        firstAsset.Currency.Should().NotBeNullOrEmpty();
        firstAsset.Platform.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// TC-05: Enum fields are serialized as PascalCase strings, not integers.
    /// </summary>
    [Fact]
    public async Task TC05_EnumFields_SerializedAsPascalCaseStrings()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert - find Apple Inc. entry
        var apple = assets.Single(a => a.Name == "Apple Inc.");
        apple.AssetType.Should().Be("Stock");
        apple.Currency.Should().Be("USD");
        apple.Platform.Should().Be("Xtb");
    }

    /// <summary>
    /// TC-06: Results are ordered alphabetically by name (ASC).
    /// </summary>
    [Fact]
    public async Task TC06_Results_OrderedAlphabeticallyByName()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert - PostgreSQL uses case-sensitive ordering (uppercase before lowercase)
        var names = assets.Select(a => a.Name).ToList();
        names.Should().BeEquivalentTo(
            ["Apple Inc.", "Investown Loan Alpha", "Tesla Inc.", "Vanguard FTSE All-World UCITS ETF", "iShares Core MSCI EM ETF"],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// TC-07: Nullable fields (Ticker, Exchange) are null for P2P loan.
    /// </summary>
    [Fact]
    public async Task TC07_P2pLoan_HasNullTickerAndExchange()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert
        var loan = assets.Single(a => a.Name == "Investown Loan Alpha");
        loan.Ticker.Should().BeNull();
        loan.Exchange.Should().BeNull();
    }

    /// <summary>
    /// TC-08: Non-null fields (Ticker, Exchange) are present for exchange-listed assets.
    /// </summary>
    [Fact]
    public async Task TC08_Stock_HasTickerAndExchange()
    {
        // Act
        var response = await _client.GetAsync("/api/assets");
        var assets = await DeserializeAsync(response);

        // Assert
        var apple = assets.Single(a => a.Name == "Apple Inc.");
        apple.Ticker.Should().Be("AAPL");
        apple.Exchange.Should().Be("NASDAQ");
    }

    #endregion

    #region Group 2 - Filter by Platform

    /// <summary>
    /// TC-09: platform=Xtb returns only Xtb assets.
    /// </summary>
    [Fact]
    public async Task TC09_PlatformXtb_ReturnsOnlyXtbAssets()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Xtb");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().HaveCount(2);
        assets.Should().AllSatisfy(a => a.Platform.Should().Be("Xtb"));
        assets.Select(a => a.Name).Should().BeEquivalentTo(
            ["Apple Inc.", "Vanguard FTSE All-World UCITS ETF"],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// TC-10: platform=Etoro returns only Etoro assets.
    /// </summary>
    [Fact]
    public async Task TC10_PlatformEtoro_ReturnsOnlyEtoroAssets()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Etoro");
        var assets = await DeserializeAsync(response);

        // Assert - PostgreSQL uses case-sensitive ordering (uppercase before lowercase)
        assets.Should().HaveCount(2);
        assets.Should().AllSatisfy(a => a.Platform.Should().Be("Etoro"));
        assets.Select(a => a.Name).Should().BeEquivalentTo(
            ["Tesla Inc.", "iShares Core MSCI EM ETF"],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// TC-11: platform=Investown returns only Investown assets.
    /// </summary>
    [Fact]
    public async Task TC11_PlatformInvestown_ReturnsOnlyInvestownAssets()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Investown");
        var assets = await DeserializeAsync(response);

        // Assert
        assets.Should().HaveCount(1);
        var loan = assets.Single();
        loan.Platform.Should().Be("Investown");
        loan.Name.Should().Be("Investown Loan Alpha");
    }

    /// <summary>
    /// TC-12: platform filter is case-SENSITIVE in .NET 10 (platform=xtb returns 400).
    /// ASP.NET Core 10 uses System.Text.Json enum binding which is case-sensitive.
    /// Documenting this behavior — users must provide exact PascalCase value (Xtb not xtb).
    /// </summary>
    [Fact]
    public async Task TC12_PlatformFilter_IsCaseSensitive_LowercaseReturns400()
    {
        // Act - lowercase xtb is NOT valid in .NET 10 (case-sensitive binding)
        var response = await _client.GetAsync("/api/assets?platform=xtb");

        // Assert - 400 Bad Request because "xtb" doesn't match "Xtb"
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// TC-13: platform accepts integer value (platform=1 maps to Xtb).
    /// </summary>
    [Fact]
    public async Task TC13_PlatformFilter_AcceptsIntegerValue()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=1");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().HaveCount(2);
        assets.Should().AllSatisfy(a => a.Platform.Should().Be("Xtb"));
    }

    #endregion

    #region Group 3 - Filter by AssetType

    /// <summary>
    /// TC-14: assetType=Stock returns only stocks.
    /// </summary>
    [Fact]
    public async Task TC14_AssetTypeStock_ReturnsOnlyStocks()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?assetType=Stock");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().HaveCount(2);
        assets.Should().AllSatisfy(a => a.AssetType.Should().Be("Stock"));
        assets.Select(a => a.Name).Should().BeEquivalentTo(
            ["Apple Inc.", "Tesla Inc."],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// TC-15: assetType=Etf returns only ETFs.
    /// </summary>
    [Fact]
    public async Task TC15_AssetTypeEtf_ReturnsOnlyEtfs()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?assetType=Etf");
        var assets = await DeserializeAsync(response);

        // Assert - PostgreSQL uses case-sensitive ordering (uppercase before lowercase)
        assets.Should().HaveCount(2);
        assets.Should().AllSatisfy(a => a.AssetType.Should().Be("Etf"));
        assets.Select(a => a.Name).Should().BeEquivalentTo(
            ["Vanguard FTSE All-World UCITS ETF", "iShares Core MSCI EM ETF"],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// TC-16: assetType=P2pLoan returns only P2P loans.
    /// </summary>
    [Fact]
    public async Task TC16_AssetTypeP2pLoan_ReturnsOnlyP2pLoans()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?assetType=P2pLoan");
        var assets = await DeserializeAsync(response);

        // Assert
        assets.Should().HaveCount(1);
        var loan = assets.Single();
        loan.AssetType.Should().Be("P2pLoan");
        loan.Name.Should().Be("Investown Loan Alpha");
    }

    #endregion

    #region Group 4 - Combined Filters

    /// <summary>
    /// TC-17: platform=Xtb and assetType=Stock returns 1 asset.
    /// </summary>
    [Fact]
    public async Task TC17_PlatformXtbAndAssetTypeStock_ReturnsOneAsset()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Xtb&assetType=Stock");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().HaveCount(1);
        var apple = assets.Single();
        apple.Name.Should().Be("Apple Inc.");
        apple.Platform.Should().Be("Xtb");
        apple.AssetType.Should().Be("Stock");
    }

    /// <summary>
    /// TC-18: platform=Etoro and assetType=Etf returns 1 asset.
    /// </summary>
    [Fact]
    public async Task TC18_PlatformEtoroAndAssetTypeEtf_ReturnsOneAsset()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Etoro&assetType=Etf");
        var assets = await DeserializeAsync(response);

        // Assert
        assets.Should().HaveCount(1);
        var etf = assets.Single();
        etf.Name.Should().Be("iShares Core MSCI EM ETF");
        etf.Platform.Should().Be("Etoro");
        etf.AssetType.Should().Be("Etf");
    }

    /// <summary>
    /// TC-19: platform=Investown and assetType=P2pLoan returns 1 asset.
    /// </summary>
    [Fact]
    public async Task TC19_PlatformInvestownAndAssetTypeP2pLoan_ReturnsOneAsset()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Investown&assetType=P2pLoan");
        var assets = await DeserializeAsync(response);

        // Assert
        assets.Should().HaveCount(1);
        var loan = assets.Single();
        loan.Name.Should().Be("Investown Loan Alpha");
        loan.Ticker.Should().BeNull();
        loan.Exchange.Should().BeNull();
    }

    /// <summary>
    /// TC-20: platform=Xtb and assetType=P2pLoan returns empty array (no match).
    /// </summary>
    [Fact]
    public async Task TC20_PlatformXtbAndAssetTypeP2pLoan_ReturnsEmptyArray()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Xtb&assetType=P2pLoan");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().BeEmpty();
    }

    #endregion

    #region Group 5 - Empty / No-Match Results

    /// <summary>
    /// TC-21: Response body for empty result is exactly an empty JSON array.
    /// </summary>
    [Fact]
    public async Task TC21_EmptyResult_ReturnsEmptyJsonArray()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=Xtb&assetType=P2pLoan");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().StartWith("application/json");

        var assets = await DeserializeAsync(response);
        assets.Should().BeEmpty();
    }

    #endregion

    #region Group 6 - Invalid Filter Values

    /// <summary>
    /// TC-22: platform=InvalidString returns 400.
    /// </summary>
    [Fact]
    public async Task TC22_InvalidPlatformString_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=NotAPlatform");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// TC-23: assetType=InvalidString returns 400.
    /// </summary>
    [Fact]
    public async Task TC23_InvalidAssetTypeString_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?assetType=ThisIsNotAnAssetType");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// TC-24: platform=999 (out-of-range integer) returns 200 with empty array.
    /// ASP.NET Core binds integer strings to enums without range validation.
    /// </summary>
    [Fact]
    public async Task TC24_OutOfRangeIntegerPlatform_Returns200WithEmptyArray()
    {
        // Act
        var response = await _client.GetAsync("/api/assets?platform=999");
        var assets = await DeserializeAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        assets.Should().BeEmpty();
    }

    #endregion

    #region Group 7 - Future Auth (Placeholder)

    /// <summary>
    /// TC-25: [Skip] Unauthenticated request returns 401 when auth is wired.
    /// </summary>
    [Fact(Skip = "Blocked by T-07: JWT bearer auth not yet configured. Remove [Skip] after T-07 lands and RequireAuthorization() is added to the AssetsFeature route group.")]
    public async Task TC25_UnauthenticatedRequest_Returns401WhenAuthIsWired()
    {
        // Future arrange: Remove AllowAnonymous / add RequireAuthorization() to route group
        // Future act: GET /api/assets with no Authorization header
        // Future assert: Status == 401 Unauthorized

        var response = await _client.GetAsync("/api/assets");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Helper Methods

    private static async Task<List<AssetDto>> DeserializeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<AssetDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// Local DTO for deserializing the response in tests.
    /// </summary>
    private record AssetDto(
        Guid Id,
        string? Ticker,
        string Name,
        string AssetType,
        string? Exchange,
        string Currency,
        string Platform);

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Web application factory for Testing environment with Testcontainers PostgreSQL.
    /// Seeds 5 test assets covering different platforms and asset types.
    /// </summary>
    public class TestFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("majetrack_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        /// <summary>
        /// Deterministic IDs for test assets.
        /// </summary>
        public static readonly Guid AppleId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        /// <summary>
        /// ID for iShares Core MSCI EM ETF.
        /// </summary>
        public static readonly Guid ISharesId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        /// <summary>
        /// ID for Investown Loan Alpha.
        /// </summary>
        public static readonly Guid InvestownLoanId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        /// <summary>
        /// ID for Tesla Inc.
        /// </summary>
        public static readonly Guid TeslaId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        /// <summary>
        /// ID for Vanguard FTSE All-World UCITS ETF.
        /// </summary>
        public static readonly Guid VanguardId = Guid.Parse("00000000-0000-0000-0000-000000000005");

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _postgresContainer.StartAsync();
        }

        /// <inheritdoc />
        public new async Task DisposeAsync()
        {
            await _postgresContainer.DisposeAsync();
            await base.DisposeAsync();
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<MajetrackDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                // Add PostgreSQL via Testcontainers
                services.AddDbContext<MajetrackDbContext>(options =>
                    options.UseNpgsql(_postgresContainer.GetConnectionString())
                           .UseSnakeCaseNamingConvention());
            });
        }

        /// <inheritdoc />
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);

            // Ensure database is created and seeded
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MajetrackDbContext>();
            db.Database.EnsureCreated();

            // Seed test assets
            SeedAssets(db);

            return host;
        }

        private static void SeedAssets(MajetrackDbContext db)
        {
            var now = DateTimeOffset.UtcNow;

            db.Assets.AddRange(
                new Asset
                {
                    Id = AppleId,
                    Name = "Apple Inc.",
                    Platform = Platform.Xtb,
                    AssetType = AssetType.Stock,
                    Currency = Currency.USD,
                    Ticker = "AAPL",
                    Exchange = "NASDAQ",
                    CreatedAt = now
                },
                new Asset
                {
                    Id = ISharesId,
                    Name = "iShares Core MSCI EM ETF",
                    Platform = Platform.Etoro,
                    AssetType = AssetType.Etf,
                    Currency = Currency.USD,
                    Ticker = "IEMG",
                    Exchange = "NYSE",
                    CreatedAt = now
                },
                new Asset
                {
                    Id = InvestownLoanId,
                    Name = "Investown Loan Alpha",
                    Platform = Platform.Investown,
                    AssetType = AssetType.P2pLoan,
                    Currency = Currency.CZK,
                    Ticker = null,
                    Exchange = null,
                    CreatedAt = now
                },
                new Asset
                {
                    Id = TeslaId,
                    Name = "Tesla Inc.",
                    Platform = Platform.Etoro,
                    AssetType = AssetType.Stock,
                    Currency = Currency.USD,
                    Ticker = "TSLA",
                    Exchange = "NASDAQ",
                    CreatedAt = now
                },
                new Asset
                {
                    Id = VanguardId,
                    Name = "Vanguard FTSE All-World UCITS ETF",
                    Platform = Platform.Xtb,
                    AssetType = AssetType.Etf,
                    Currency = Currency.EUR,
                    Ticker = "VWCE.DE",
                    Exchange = "XETRA",
                    CreatedAt = now
                }
            );

            db.SaveChanges();
        }
    }

    #endregion
}
