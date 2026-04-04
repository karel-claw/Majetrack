namespace Majetrack.Infrastructure.ExternalServices.CnbExchangeRateProvider;

/// <summary>
/// Configuration options for <see cref="CnbExchangeRateProvider"/>.
/// </summary>
public class CnbExchangeRateProviderOptions
{
    public const string SectionName = "CnbExchangeRateProvider";

    /// <summary>
    /// Base URL for the CNB daily exchange rates API.
    /// Default: https://www.cnb.cz/en/financial-markets/foreign-exchange-market/central-bank-exchange-rate-fixing/central-bank-exchange-rate-fixing/daily.txt
    /// </summary>
    public string BaseUrl { get; set; } =
        "https://www.cnb.cz/en/financial-markets/foreign-exchange-market/central-bank-exchange-rate-fixing/central-bank-exchange-rate-fixing/daily.txt";

    /// <summary>
    /// How long to cache rates. Default: 24 hours.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum number of previous business days to try when falling back. Default: 7.
    /// </summary>
    public int MaxFallbackDays { get; set; } = 7;
}
