using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// SEAT's overview page has plain static anchors (no JSON-in-attribute hydration like Škoda): links to
/// per-model sub-pages that in turn list direct PDF links using the same filename convention as
/// VW/Audi/Cupra. The overview also links a general "Emergency Response Guide" PDF, which is
/// deliberately not collected - ERGs aren't rescue sheets (see RescueDocumentClassifier).
/// </summary>
public sealed class SeatRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<SeatRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string OverviewUrl = "https://www.seat.de/kontakt/downloads/rettungsblaetter";

    public override Brand Brand => Brand.Seat;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var context = BrowsingContext.New(Configuration.Default);

        var overviewHtml = await client.GetStringAsync(OverviewUrl, ct);
        var overviewDoc = await context.OpenAsync(req => req.Content(overviewHtml), ct);

        var entries = new List<RescueCardEntry>();

        var modelPageUrls = overviewDoc.QuerySelectorAll("a.link-arrow[href*='/rettungsblaetter/']")
            .Select(a => a.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => HttpDownloadHelper.ResolveUrl(OverviewUrl, href!))
            .Distinct()
            .ToList();

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Seat_ModelPagesFound", modelPageUrls.Count));

        foreach (var modelPageUrl in modelPageUrls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await client.GetStringAsync(modelPageUrl, ct);
                var document = await context.OpenAsync(req => req.Content(html), ct);

                foreach (var anchor in document.QuerySelectorAll("a[href$='.pdf']"))
                {
                    var href = anchor.GetAttribute("href");
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    var absoluteUrl = HttpDownloadHelper.ResolveUrl(modelPageUrl, href);
                    var parsed = StandardRescueSheetFilenameParser.Parse(absoluteUrl);

                    if (!string.Equals(parsed.LanguageCode, "DE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entries.Add(new RescueCardEntry(Brand.Seat, modelPageUrl, absoluteUrl, Path.GetFileName(new Uri(absoluteUrl).AbsolutePath), parsed));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Seat_ModelPageReadFailed", modelPageUrl));
            }
        }

        return entries;
    }
}
