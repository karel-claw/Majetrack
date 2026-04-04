namespace Majetrack.Infrastructure.ExternalServices.TwelveDataPriceProvider;

/// <summary>
/// Configuration options for <see cref="TwelveDataPriceProvider"/>.
/// </summary>
public class TwelveDataPriceProviderOptions
{
    public const string SectionName = "TwelveDataPriceProvider";

    /// <summary>
    /// TwelveData API key. Required.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the TwelveData API. Default: https://api.twelvedata.com
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.twelvedata.com";

    /// <summary>
    /// How long to cache prices. Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
