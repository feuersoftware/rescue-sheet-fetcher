using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Audi publishes a single static server-rendered page with ~700 direct PDF links, no auth/JS
/// required - the simplest of the five brand sources. Filenames follow the same convention as VW/Cupra.
/// </summary>
public sealed class AudiRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<AudiRescueCardSource> logger) : IRescueCardSource
{
    private const string PageUrl = "https://www.audi.com/en/information-on-accident-rescue-17123";

    public Brand Brand => Brand.Audi;

    public async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var html = await client.GetStringAsync(PageUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var entries = new List<RescueCardEntry>();
        foreach (var anchor in document.QuerySelectorAll("a[href$='.pdf']"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absoluteUrl = HttpDownloadHelper.ResolveUrl(PageUrl, href);
            var parsed = AudiFilenameParser.Parse(absoluteUrl);

            // German-only per requirement; Audi's page mixes every language on one page.
            if (!string.Equals(parsed.LanguageCode, "DE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new RescueCardEntry(Brand.Audi, PageUrl, absoluteUrl, Path.GetFileName(new Uri(absoluteUrl).AbsolutePath), parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Audi_DiscoveredCount", entries.Count));
        return entries;
    }

    public async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl!, ct);
    }
}
