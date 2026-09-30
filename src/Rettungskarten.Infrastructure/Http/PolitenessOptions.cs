namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// Per-host politeness configuration. KBA's robots.txt requires a 30s crawl-delay on statistics paths;
/// everything else gets a small default delay as general good manners since none of the manufacturer
/// sites publish a crawl-delay of their own.
/// </summary>
public sealed record PolitenessOptions
{
    public TimeSpan DefaultDelay { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyDictionary<string, TimeSpan> PerHostDelay { get; init; } =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["www.kba.de"] = TimeSpan.FromSeconds(30)
        };

    /// <summary>
    /// Identifies this tool to the sites it fetches from. Override via appsettings.json with a real
    /// contact URL/address before running this against production sites, per good scraping etiquette.
    /// </summary>
    public string UserAgent { get; init; } = $"{ProductToken}/1.0 (fire-brigade rescue-data fetcher; set a real contact URL/address in appsettings.json)";

    /// <summary>
    /// The robots.txt product token this tool honors rules for (see <see cref="RobotsTxtRules"/>) -
    /// also used for the browser-header client, whose User-Agent can't carry it.
    /// </summary>
    public const string ProductToken = "RettungskartenTool";

    /// <summary>
    /// User-Agent for <see cref="RettungskartenHttpClient.BrowserName"/> only. Akamai-fronted
    /// manufacturer sites (the Stellantis brand sites, Volvo, Tesla, Ford's content CDN) answer any
    /// request without a browser-like header set with 403 - verified to be the header set, not the TLS
    /// fingerprint, on Windows; see that client's registration for the full list.
    /// </summary>
    public string BrowserUserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    public TimeSpan GetDelayFor(string host) => PerHostDelay.GetValueOrDefault(host, DefaultDelay);
}
