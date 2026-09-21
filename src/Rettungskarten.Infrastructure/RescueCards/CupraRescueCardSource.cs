using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Cupra's Swiss site is statically pre-rendered with direct PDF links using the same filename
/// convention as VW/Audi, no auth required - used as the sole source for V1. The Austrian site embeds
/// the same data as JSON inside a Next.js RSC flight payload, but its PDF download endpoint redirects
/// to an identity/auth gateway (confirmed in research) and is not wired up here; extending to it later
/// would only add metadata-only entries for models already covered here from the CH catalog.
/// </summary>
public sealed class CupraRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<CupraRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string PageUrl = "https://www.cupraofficial.ch/de/services/rettungsblaetter";

    public override Brand Brand => Brand.Cupra;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = HttpClientFactory.CreateClient(RettungskartenHttpClient.Name);
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
            var parsed = VwSeatCupraFilenameParser.Parse(absoluteUrl);

            if (!string.Equals(parsed.LanguageCode, "DE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new RescueCardEntry(Brand.Cupra, PageUrl, absoluteUrl, Path.GetFileName(new Uri(absoluteUrl).AbsolutePath), parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Cupra_DiscoveredCount", entries.Count));
        return entries;
    }
}
