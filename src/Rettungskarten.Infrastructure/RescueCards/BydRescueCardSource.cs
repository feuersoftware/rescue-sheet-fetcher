using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// BYD has no German-market page with rescue sheets (verified 2026-09-29: bydauto.de times out,
/// byd-auto.de doesn't resolve, byd.com/de is a client-rendered app without any document list). The
/// official German-language source is the Austrian importer's Webflow site, whose downloads page
/// (bydauto.at/downloads) lists every document per model as a server-rendered CMS collection: one
/// <c>.download_item</c> per document with its title in an <c>h3</c> ("Rettungskarte ATTO 2 DM-i",
/// "Bedienungsanleitung ...", "Kurzanleitung ...") and a direct link to the Webflow CDN
/// (<c>a.download-link</c>). Only items whose title names a rescue sheet ("Rettungskarte",
/// "Rettungsblatt", "Rettungsdatenblatt") are taken; the manuals, quick guides and brochures next to
/// them are not. The CDN filenames ("..._BYD SEAL U DM-i Rettungsblatt-Linkslenker-DE.pdf") carry a
/// publication date at best, never a build year, so no year is set. BYD's European range is fully
/// electric except the "DM-i" plug-in hybrids, which the title always marks.
/// </summary>
public sealed class BydRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<BydRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    internal const string PageUrl = "https://www.bydauto.at/downloads";

    private static readonly Regex RescueTitle = new(@"^\s*Rettungs(?:karte|blatt|datenblatt)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Trim/edition words that follow the model name in a title: they go to Variant only, so "ATTO 3
    // EVO" or "ATTO 2 Comfort" are still the model KBA counts as "ATTO 3"/"ATTO 2".
    private static readonly Regex TrimWords = new(@"\b(?:EVO|Comfort|Touring)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DmI = new(@"\bDM-?i\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.BYD;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var html = await client.GetStringAsync(PageUrl, ct);
        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html).Address(PageUrl), ct);

        var entries = new List<RescueCardEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.QuerySelectorAll(".download_item"))
        {
            var title = Whitespace.Replace(item.QuerySelector(".download_title")?.TextContent ?? string.Empty, " ").Trim();
            var href = item.QuerySelector("a.download-link[href]")?.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href) || !RescueTitle.IsMatch(title))
            {
                continue;
            }

            var url = HttpDownloadHelper.ResolveUrl(PageUrl, href);
            var fileName = HttpDownloadHelper.GetFileName(url);
            if (!RescueDocumentClassifier.IsRescueSheet(title, fileName) || !seen.Add(url))
            {
                continue;
            }

            entries.Add(new RescueCardEntry(Brand.BYD, PageUrl, url, fileName, ParseTitle(title)));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    /// <summary>"Rettungskarte SEAL 6 DM-i Touring" -> model "SEAL 6", fuel "DM-i", body "Touring".</summary>
    internal static ParsedModelInfo ParseTitle(string title)
    {
        var variant = RescueTitle.Replace(title, string.Empty).Trim();
        var isPhev = DmI.IsMatch(variant);
        var model = Whitespace.Replace(TrimWords.Replace(DmI.Replace(variant, " "), " "), " ").Trim();

        return new ParsedModelInfo(
            ModelName: model.Length > 0 ? model : null,
            Variant: variant.Length > 0 ? variant : null,
            BodyType: VehicleAttributeTextHelper.ExtractLastWordMatch(variant, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: null,
            BuildYearTo: null,
            Doors: null,
            FuelType: isPhev ? "DM-i" : "Elektro",
            LanguageCode: "DE",
            ParseConfidence: model.Length > 0 ? ParseConfidence.Heuristic : ParseConfidence.Unparsed);
    }
}
