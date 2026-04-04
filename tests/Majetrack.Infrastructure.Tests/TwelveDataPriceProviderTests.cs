using System.Net;
using FluentAssertions;
using Majetrack.Infrastructure.ExternalServices.TwelveDataPriceProvider;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Majetrack.Infrastructure.Tests;

/// <summary>
/// Tests for TwelveDataPriceProvider.
/// TC920 = Happy Path, TC921 = Error Path.
/// </summary>
public class TwelveDataPriceProviderTests
{
    // ── Sample TwelveData response bodies ─────────────────────────────────────

    private const string ValidPriceResponse = """{"price":"182.4500"}""";
    private const string InvalidSymbolResponse = """{"code":400,"message":"**symbol** not found: INVALID","status":"error"}""";

    // ── Factory helper ────────────────────────────────────────────────────────

    private static (TwelveDataPriceProvider provider, Mock<HttpMessageHandler> handlerMock)
        CreateProvider(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK,
            TwelveDataPriceProviderOptions? opts = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(opts ?? new TwelveDataPriceProviderOptions { ApiKey = "test-key" });
        var logger = NullLogger<TwelveDataPriceProvider>.Instance;

        var provider = new TwelveDataPriceProvider(httpClient, cache, options, logger);
        return (provider, handlerMock);
    }

    // ── TC920: Happy Path ─────────────────────────────────────────────────────

    [Fact(DisplayName = "TC920: GetPrice_ValidSymbol_ReturnsPrice")]
    public async Task TC920_GetPrice_ValidSymbol_ReturnsPrice()
    {
        var (provider, _) = CreateProvider(ValidPriceResponse);

        var price = await provider.GetPriceAsync("AAPL");

        price.Should().NotBeNull();
        price!.Value.Should().BeApproximately(182.45m, 0.001m);
    }

    // ── TC921: Error Path ─────────────────────────────────────────────────────

    [Fact(DisplayName = "TC921: GetPrice_InvalidSymbol_ReturnsNull")]
    public async Task TC921_GetPrice_InvalidSymbol_ReturnsNull()
    {
        var (provider, _) = CreateProvider(InvalidSymbolResponse);

        var price = await provider.GetPriceAsync("INVALID");

        price.Should().BeNull();
    }
}
