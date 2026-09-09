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
    public string UserAgent { get; init; } = "RettungskartenTool/1.0 (fire-brigade rescue-data fetcher; set a real contact URL/address in appsettings.json)";

    public TimeSpan GetDelayFor(string host) => PerHostDelay.GetValueOrDefault(host, DefaultDelay);
}
