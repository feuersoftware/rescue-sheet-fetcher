using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>
/// Maserati Germany publishes no per-model rescue sheets: its "Aftersales Services" page
/// (maserati.com/de/de/shopping-tools/aftersales-services) has one "Rettungsdatenblätter" download, a
/// single six-page PDF from 05/2016 (Maserati Deutschland GmbH with Moditech) with one page per model -
/// GranCabrio, GranTurismo, Ghibli, Levante and two Quattroporte generations. The download link itself
/// has no text (an icon button inside a "Rettungsdatenblätter" teaser), and the same page also links an
/// unrelated insurance PDF ("Maserati_Police_DE.pdf"), so the link is recognized by its filename. It is
/// kept as one <see cref="DocumentScope.Combined"/> entry; `split maserati`
/// (<see cref="Splitting.MaseratiCombinedPdfLayout"/>) turns it into one card per model.
///
/// maserati.com is Akamai-fronted (403 without browser headers); its robots.txt allows the PDF.
/// </summary>
public sealed class MaseratiRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<MaseratiRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    public const string CombinedModelName = "All Models";

    private const string PageUrl = "https://www.maserati.com/de/de/shopping-tools/aftersales-services";

    public override Brand Brand => Brand.Maserati;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string DiscoveryClientName => RettungskartenHttpClient.BrowserName;

    protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        HttpDownloadHelper.GetFileName(absoluteUrl).Contains("Rettungsdatenbl", StringComparison.OrdinalIgnoreCase);

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        new(CombinedModelName,
            Variant: Path.GetFileNameWithoutExtension(HttpDownloadHelper.GetFileName(absoluteUrl)).Replace('_', ' '),
            BodyType: null, BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null,
            LanguageCode: "DE", ParseConfidence.Heuristic);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);
        return entries.Select(e => e with { Scope = DocumentScope.Combined }).ToList();
    }
}
