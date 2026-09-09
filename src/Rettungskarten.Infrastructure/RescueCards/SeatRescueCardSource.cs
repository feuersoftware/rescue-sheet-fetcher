using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// SEAT's overview page has plain static anchors (no JSON-in-attribute hydration like Škoda): one
/// direct link to a general multi-language "Emergency Response Guide" PDF, plus links to per-model
/// sub-pages that in turn list direct PDF links using the same filename convention as VW/Audi/Cupra.
/// </summary>
public sealed class SeatRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<SeatRescueCardSource> logger) : IRescueCardSource
{
    private const string OverviewUrl = "https://www.seat.de/kontakt/downloads/rettungsblaetter";
    private const string GeneralGuideModelName = "General Guide";

    public Brand Brand => Brand.Seat;

    public async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var context = BrowsingContext.New(Configuration.Default);

        var overviewHtml = await client.GetStringAsync(OverviewUrl, ct);
        var overviewDoc = await context.OpenAsync(req => req.Content(overviewHtml), ct);

        var entries = new List<RescueCardEntry>();

        // The general guide is linked directly from the overview page.
        foreach (var anchor in overviewDoc.QuerySelectorAll("a[href*='emergency-response-guide-de.pdf']"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absoluteUrl = HttpDownloadHelper.ResolveUrl(OverviewUrl, href);
            // ModelName is domain data (it feeds RescueCardIdBuilder's id/folder-name and
            // BundlePriorityCalculator's KBA matching), not display text - it must stay invariant
            // across --lang, or the same real-world PDF would get a different id per language.
            var parsed = new ParsedModelInfo(
                ModelName: GeneralGuideModelName, Variant: null, BodyType: null,
                BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null,
                LanguageCode: "DE", ParseConfidence.High);
            entries.Add(new RescueCardEntry(Brand.Seat, OverviewUrl, absoluteUrl, "seat-emergency-response-guide-de.pdf", parsed));
            break; // one general guide is enough even if the page links it twice (nav + CTA button)
        }

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
                    var parsed = VwSeatCupraFilenameParser.Parse(absoluteUrl);

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

    public async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl!, ct);
    }
}
