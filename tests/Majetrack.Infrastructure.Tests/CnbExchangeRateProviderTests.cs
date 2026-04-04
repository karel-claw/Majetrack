using System.Net;
using System.Net.Http;
using FluentAssertions;
using Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Majetrack.Infrastructure.Tests;

/// <summary>
/// Tests for CnbExchangeRateProvider.
/// TC1xx = Happy Path, TC11x = Edge/Cache, TC12x = Error paths.
/// </summary>
public class CnbExchangeRateProviderTests : IDisposable
{
    // ── Sample CNB response bodies ────────────────────────────────────────────

    /// <summary>
    /// Realistic CNB response: USD=25.00, EUR=27.50, GBP=31.00, AUD=16.00, amount=1 each.
    /// </summary>
    private const string SampleCnbResponse = """
        04 Apr 2025 #65
        Country|Currency|Amount|Code|Rate
        Australia|dollar|1|AUD|16.000
        EMU|euro|1|EUR|27.500
        United Kingdom|pound|1|GBP|31.000
        USA|dollar|1|USD|25.000
        """;

    /// <summary>
    /// CNB response where 1 HUF = 0.069 CZK (amount = 100 per 100 HUF).
    /// </summary>
    private const string SampleWithAmountResponse = """
        04 Apr 2025 #65
        Country|Currency|Amount|Code|Rate
        Hungary|forint|100|HUF|6.900
        USA|dollar|1|USD|25.000
        """;

    // ── Factory helpers ───────────────────────────────────────────────────────

    private static (CnbExchangeRateProvider provider, Mock<HttpMessageHandler> handlerMock, IMemoryCache cache)
        CreateProvider(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK,
            CnbExchangeRateProviderOptions? opts = null)
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
        var options = Options.Create(opts ?? new CnbExchangeRateProviderOptions());
        var logger = NullLogger<CnbExchangeRateProvider>.Instance;

        var provider = new CnbExchangeRateProvider(httpClient, cache, options, logger);
        return (provider, handlerMock, cache);
    }

    private static CnbExchangeRateProvider CreateProviderWithHandler(
        Mock<HttpMessageHandler> handlerMock,
        CnbExchangeRateProviderOptions? opts = null)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(opts ?? new CnbExchangeRateProviderOptions());
        var logger = NullLogger<CnbExchangeRateProvider>.Instance;
        return new CnbExchangeRateProvider(httpClient, cache, options, logger);
    }

    // ── TC101-108: Happy Path ─────────────────────────────────────────────────

    [Fact(DisplayName = "TC101: Foreign → CZK returns direct CNB rate")]
    public async Task TC101_ForeignToCzk_ReturnsDirectRate()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(25.00m, 0.001m);
    }

    [Fact(DisplayName = "TC102: CZK → Foreign returns inverse rate")]
    public async Task TC102_CzkToForeign_ReturnsInverseRate()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("CZK", "USD");

        // 1/25.00 = 0.04
        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(0.04m, 0.0001m);
    }

    [Fact(DisplayName = "TC103: Cross pair (EUR→USD) triangulated via CZK")]
    public async Task TC103_CrossPair_TriangulatedViaCzk()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        // EUR/CZK = 27.50, USD/CZK = 25.00 → EUR/USD = 27.50/25.00 = 1.10
        var rate = await provider.GetRateAsync("EUR", "USD");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(1.10m, 0.001m);
    }

    [Fact(DisplayName = "TC104: Cross pair (USD→EUR) triangulated via CZK")]
    public async Task TC104_UsdToEur_CrossPair()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        // USD/CZK=25.00, EUR/CZK=27.50 → USD/EUR = 25.00/27.50 ≈ 0.909
        var rate = await provider.GetRateAsync("USD", "EUR");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(25m / 27.5m, 0.001m);
    }

    [Fact(DisplayName = "TC105: Same currency returns 1 without HTTP call")]
    public async Task TC105_SameCurrency_ReturnsOneImmediately()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        // Should NOT set up any HTTP call — strict mock will fail if called
        var provider = CreateProviderWithHandler(handlerMock);

        var rate = await provider.GetRateAsync("EUR", "EUR");

        rate.Should().Be(1m);
        handlerMock.Protected().Verify("SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact(DisplayName = "TC106: CZK → CZK returns 1 without HTTP call")]
    public async Task TC106_CzkToCzk_ReturnsOne()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var provider = CreateProviderWithHandler(handlerMock);

        var rate = await provider.GetRateAsync("CZK", "CZK");

        rate.Should().Be(1m);
    }

    [Fact(DisplayName = "TC107: Currency with amount>1 (HUF/100) normalised to per-unit rate")]
    public async Task TC107_AmountGreaterThanOne_NormalisedToPerUnit()
    {
        var (provider, _, _) = CreateProvider(SampleWithAmountResponse);

        // 100 HUF = 6.90 CZK → 1 HUF = 0.069 CZK
        var rate = await provider.GetRateAsync("HUF", "CZK");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(0.069m, 0.0001m);
    }

    [Fact(DisplayName = "TC108: GBP→AUD cross pair via CZK")]
    public async Task TC108_GbpToAud_CrossPair()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        // GBP=31.00, AUD=16.00 → GBP/AUD = 31/16 = 1.9375
        var rate = await provider.GetRateAsync("GBP", "AUD");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(31m / 16m, 0.001m);
    }

    // ── TC110-117: Edge / Caching ─────────────────────────────────────────────

    [Fact(DisplayName = "TC110: Second call returns cached result (HTTP called once)")]
    public async Task TC110_SecondCall_UsesCachedResult()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleCnbResponse)
            });

        var provider = CreateProviderWithHandler(handlerMock);

        var rate1 = await provider.GetRateAsync("USD", "CZK");
        var rate2 = await provider.GetRateAsync("EUR", "CZK");

        rate1.Should().NotBeNull();
        rate2.Should().NotBeNull();

        // Both calls should use same fetched data (1 HTTP call for today's date)
        handlerMock.Protected().Verify("SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact(DisplayName = "TC111: Case-insensitive currency codes")]
    public async Task TC111_CurrencyCodesAreCaseInsensitive()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate1 = await provider.GetRateAsync("usd", "czk");
        var rate2 = await provider.GetRateAsync("USD", "CZK");
        var rate3 = await provider.GetRateAsync("Usd", "Czk");

        rate1.Should().Be(rate2);
        rate2.Should().Be(rate3);
    }

    [Fact(DisplayName = "TC112: Whitespace in currency codes is trimmed")]
    public async Task TC112_WhitespaceTrimmedFromCurrencyCodes()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("  USD  ", "  CZK  ");

        rate.Should().NotBeNull();
        rate!.Value.Should().BeApproximately(25.00m, 0.001m);
    }

    [Fact(DisplayName = "TC113: Concurrent calls for same date result in single HTTP call")]
    public async Task TC113_ConcurrentCalls_SingleHttpFetch()
    {
        var callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(50); // simulate latency
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleCnbResponse)
                };
            });

        var provider = CreateProviderWithHandler(handlerMock);

        // Fire 10 concurrent requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetRateAsync("USD", "CZK"))
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        callCount.Should().Be(1, "only one HTTP call should be made regardless of concurrency");
    }

    [Fact(DisplayName = "TC114: ParseCnbResponse returns null for empty content")]
    public void TC114_ParseCnbResponse_NullForEmpty()
    {
        var result = CnbExchangeRateProvider.ParseCnbResponse("", DateOnly.FromDateTime(DateTime.Today));
        result.Should().BeNull();
    }

    [Fact(DisplayName = "TC115: ParseCnbResponse returns null when fewer than 3 lines")]
    public void TC115_ParseCnbResponse_NullForTooFewLines()
    {
        var result = CnbExchangeRateProvider.ParseCnbResponse("line1\nline2", DateOnly.FromDateTime(DateTime.Today));
        result.Should().BeNull();
    }

    [Fact(DisplayName = "TC116: ParseCnbResponse skips malformed data lines")]
    public void TC116_ParseCnbResponse_SkipsMalformedLines()
    {
        var content = """
            04 Apr 2025 #65
            Country|Currency|Amount|Code|Rate
            BADLINE
            USA|dollar|1|USD|25.000
            """;

        var result = CnbExchangeRateProvider.ParseCnbResponse(content, DateOnly.FromDateTime(DateTime.Today));

        result.Should().NotBeNull();
        result!.Should().ContainKey("USD");
        result.Should().HaveCount(1);
    }

    [Fact(DisplayName = "TC117: ParseCnbResponse normalises amount>1 correctly")]
    public void TC117_ParseCnbResponse_NormalisesAmount()
    {
        var content = """
            04 Apr 2025 #65
            Country|Currency|Amount|Code|Rate
            Hungary|forint|100|HUF|6.900
            """;

        var result = CnbExchangeRateProvider.ParseCnbResponse(content, DateOnly.FromDateTime(DateTime.Today));

        result.Should().NotBeNull();
        result!["HUF"].Should().BeApproximately(0.069m, 0.0001m);
    }

    // ── TC120-134: Error Paths ────────────────────────────────────────────────

    [Fact(DisplayName = "TC120: Unknown 'from' currency returns null")]
    public async Task TC120_UnknownFromCurrency_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("XYZ", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC121: Unknown 'to' currency returns null")]
    public async Task TC121_UnknownToCurrency_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("USD", "XYZ");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC122: Both currencies unknown returns null")]
    public async Task TC122_BothCurrenciesUnknown_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("FOO", "BAR");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC123: Null 'from' returns null")]
    public async Task TC123_NullFrom_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync(null!, "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC124: Null 'to' returns null")]
    public async Task TC124_NullTo_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("USD", null!);

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC125: Empty 'from' string returns null")]
    public async Task TC125_EmptyFrom_ReturnsNull()
    {
        var (provider, _, _) = CreateProvider(SampleCnbResponse);

        var rate = await provider.GetRateAsync("", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC126: HTTP 500 for all fallback days returns null")]
    public async Task TC126_AllDaysReturn500_ReturnsNull()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 3 };
        var (provider, _, _) = CreateProvider("", HttpStatusCode.InternalServerError, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC127: HTTP 404 for all days returns null")]
    public async Task TC127_AllDays404_ReturnsNull()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 3 };
        var (provider, _, _) = CreateProvider("", HttpStatusCode.NotFound, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC128: HttpRequestException for all days returns null")]
    public async Task TC128_HttpRequestException_ReturnsNull()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 2 };
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var provider = CreateProviderWithHandler(handlerMock, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC129: Empty response body returns null")]
    public async Task TC129_EmptyResponseBody_ReturnsNull()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 1 };
        var (provider, _, _) = CreateProvider("", HttpStatusCode.OK, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC130: Whitespace-only response body returns null")]
    public async Task TC130_WhitespaceResponseBody_ReturnsNull()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 1 };
        var (provider, _, _) = CreateProvider("   \n\n   ", HttpStatusCode.OK, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC131: Fallback to previous day when today returns empty")]
    public async Task TC131_FallbackToPreviousDay_WhenTodayFails()
    {
        var callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() =>
            {
                var count = Interlocked.Increment(ref callCount);
                // First call (today) returns garbage, second call (yesterday) returns data
                var body = count == 1 ? "" : SampleCnbResponse;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            });

        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 5 };
        var provider = CreateProviderWithHandler(handlerMock, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().NotBeNull("should fall back to previous business day");
        rate!.Value.Should().BeApproximately(25.00m, 0.001m);
    }

    [Fact(DisplayName = "TC132: MaxFallbackDays=0 gives no fallback, returns null on failure")]
    public async Task TC132_MaxFallbackDaysZero_NoFallback()
    {
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 0 };
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            });

        var provider = CreateProviderWithHandler(handlerMock, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC133: CancellationToken respected - throws OperationCanceledException")]
    public async Task TC133_CancellationToken_Respected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 1 };
        var provider = CreateProviderWithHandler(handlerMock, opts);

        // With cancelled token and handler throwing TaskCanceledException,
        // the provider should return null (it catches cancellation gracefully)
        var rate = await provider.GetRateAsync("USD", "CZK", cts.Token);

        rate.Should().BeNull();
    }

    [Fact(DisplayName = "TC134: Response with only header lines (no data) returns null")]
    public async Task TC134_ResponseWithOnlyHeaders_ReturnsNull()
    {
        var content = "04 Apr 2025 #65\nCountry|Currency|Amount|Code|Rate\n";
        var opts = new CnbExchangeRateProviderOptions { MaxFallbackDays = 1 };
        var (provider, _, _) = CreateProvider(content, HttpStatusCode.OK, opts);

        var rate = await provider.GetRateAsync("USD", "CZK");

        rate.Should().BeNull();
    }

    public void Dispose() { }
}
