using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Porsche publishes its rescue data sheets as one combined PDF covering every current model, plus a
/// second covering classic/older models - unlike every other brand there is no per-model file. Found
/// via the "Weitere Dokumente"/"Further Documents" page (linked from Porsche's German site, which
/// redirects to the same international URL), under a "Rescue Data Sheets" entry; both PDFs are served
/// directly from Porsche's own asset CDN (assets-v2.porsche.com), not a third-party mirror. See
/// <see cref="PorscheDocumentsPageParser"/> for how the links are actually extracted from the page.
/// </summary>
public sealed class PorscheRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<PorscheRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string DocumentsPageUrl =
        "https://www.porsche.com/international/accessoriesandservice/porscheservice/vehicleinformation/documents/";

    public override Brand Brand => Brand.Porsche;

    // Both of Porsche's combined documents are large (the main one is ~55MB) - use the long-timeout
    // client rather than the shared default one (see
    // HttpServiceCollectionExtensions.AddRettungskartenHttpClient for why these are kept separate).
    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = HttpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var html = await client.GetStringAsync(DocumentsPageUrl, ct);

        var links = await PorscheDocumentsPageParser.ParseRescueDataSheetLinksAsync(html, ct);
        if (links.Count == 0)
        {
            logger.LogWarning("{Message}", Strings.Get("RescueCards_Porsche_TemplateNotFound"));
            return [];
        }

        var entries = links.Select(l =>
        {
            // ModelName is a fixed label, not a real model - it's domain data (feeds the persisted
            // id/folder name), so it must stay invariant across --lang rather than being localized,
            // same reasoning as SEAT's general guide entry.
            var isClassic = l.Text.Contains("Classic", StringComparison.OrdinalIgnoreCase);
            var modelName = isClassic ? "All Models Classic" : "All Models";

            // Unlike every other brand's German-only entries, this document's actual content is
            // English (confirmed by reading it) - Porsche does not offer a separate German file here.
            var parsed = new ParsedModelInfo(
                ModelName: modelName, Variant: l.Text, BodyType: null,
                BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null,
                LanguageCode: "EN", ParseConfidence.Heuristic);

            return new RescueCardEntry(Brand.Porsche, DocumentsPageUrl, l.Href, l.Text, parsed);
        }).ToList();

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Porsche_DiscoveredCount", entries.Count));
        return entries;
    }
}
