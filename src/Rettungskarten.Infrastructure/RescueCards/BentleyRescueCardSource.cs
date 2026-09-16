using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Bentley's "Other Vehicle Information" page has a "Rescue Sheets" accordion: one
/// <c>.bm-m-accordion__item</c> per model (heading text like "NEW CONTINENTAL GT (ICE) (2018 - 2024)"
/// or "BENTAYGA (HYBRID) (2021 - )" - powertrain and year range both embedded in the heading itself),
/// each containing several per-language PDF links. Verified empirically against the real page: every
/// per-language download URL uses a *different* naming scheme depending on when that model's sheet was
/// published (plain "{code}_{LANG}_Rescue_Card_V1.pdf", a spelled-out "..._German.pdf", a
/// "..._DE_Web.pdf" suffix, "...-German-Aug2020.pdf", and more) - there is no single URL shape that
/// reliably means "German". The one thing that IS consistent across every scheme is the link's own
/// visible button label, always the literal string "DEUTSCHE" for the German variant - so this filters
/// on that instead of the URL. Also requires the containing item's heading to state a year (every real
/// model entry does; the page's unrelated accordions - RDE data, technical/supplier info - don't),
/// which keeps this from picking up an unrelated "DEUTSCHE"-labelled link if the page ever grows one
/// elsewhere, without depending on the accordion's own auto-generated container id (not stable across
/// site rebuilds).
///
/// The label itself isn't fully trustworthy either, though: one real model ("BENTAYGA (HYBRID)
/// (2021 - )") has its "DEUTSCHE" button pointing at the exact same URL as its "中文" button, whose
/// filename literally says "chinese_simplified" - a mislabeling bug on Bentley's own page, found by
/// comparing hrefs across every language button in the same item. Handing that out as a German rescue
/// card would be actively wrong, not just missing, so a "DEUTSCHE" link sharing its href with a
/// differently-labelled button in the same item is treated as unverifiable and skipped (with a
/// warning) rather than trusted.
/// </summary>
public sealed class BentleyRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<BentleyRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string PageUrl = "https://www.bentleymotors.com/en/pages/other-vehicle-information.html";
    private const string GermanLabel = "DEUTSCHE";

    private static readonly Regex PowertrainPattern = new(@"\((ICE|HYBRID)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParentheticalPattern = new(@"\s*\([^)]*\)", RegexOptions.Compiled);

    public override Brand Brand => Brand.Bentley;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = HttpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var html = await client.GetStringAsync(PageUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var entries = new List<RescueCardEntry>();
        foreach (var item in document.QuerySelectorAll(".bm-m-accordion__item"))
        {
            var headingText = item.QuerySelector(".bm-m-accordion__item-title")?.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(headingText))
            {
                continue;
            }

            var yearRange = ModelYearRangeTextHelper.Extract(headingText);
            if (yearRange is { From: null, To: null })
            {
                continue; // not a model entry (e.g. "REAL DRIVING EMISSIONS (RDE)", "Technical Data...")
            }

            var languageLinks = item.QuerySelectorAll("a[href$='.pdf']")
                .Select(a => (Href: a.GetAttribute("href"), Label: a.QuerySelector(".bm-e-button__label")?.TextContent.Trim()))
                .Where(l => !string.IsNullOrWhiteSpace(l.Href))
                .ToList();

            var href = languageLinks.FirstOrDefault(l => string.Equals(l.Label, GermanLabel, StringComparison.OrdinalIgnoreCase)).Href;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue; // this model doesn't offer a German sheet
            }

            if (languageLinks.Any(l => l.Href == href && !string.Equals(l.Label, GermanLabel, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("{Message}", Strings.Get("RescueCards_Bentley_AmbiguousGermanLink", headingText));
                continue;
            }

            var absoluteUrl = HttpDownloadHelper.ResolveUrl(PageUrl, href);
            var fuelTypeMatch = PowertrainPattern.Match(headingText);
            var modelName = ParentheticalPattern.Replace(headingText, string.Empty).Trim();

            var parsed = new ParsedModelInfo(
                ModelName: modelName, Variant: headingText, BodyType: null,
                BuildYearFrom: yearRange.From, BuildYearTo: yearRange.To, Doors: null,
                FuelType: fuelTypeMatch.Success ? fuelTypeMatch.Groups[1].Value : null,
                LanguageCode: "DE", ParseConfidence.Heuristic);

            entries.Add(new RescueCardEntry(Brand.Bentley, PageUrl, absoluteUrl, headingText, parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Bentley_DiscoveredCount", entries.Count));
        return entries;
    }
}
