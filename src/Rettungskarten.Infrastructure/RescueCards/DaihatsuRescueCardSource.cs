using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Daihatsu left the European market in 2013; its German importer (Emil Frey) still runs daihatsu.de
/// for service and parts, and its main navigation links one combined PDF,
/// "daihatsu_rettungsdatenblaetter_de_at.pdf" (verified 2026-09-29: 25 pages, ~5.4MB - a cover, an
/// overview table, then one scanned rescue sheet per model generation from Applause to YRV, dated
/// 12/2009 plus the 2011 Charade).
///
/// Discovery reads the homepage and takes that link rather than hard-coding the PDF URL, so the weekly
/// link check notices when the importer moves or drops the file. The document is kept as one
/// <see cref="DocumentScope.Combined"/> entry; <c>split daihatsu</c> cuts it into per-model parts
/// using <see cref="Splitting.DaihatsuCombinedPdfLayout"/>, which reads the overview table (the sheets
/// themselves are images without text or bookmarks).
/// </summary>
public sealed class DaihatsuRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<DaihatsuRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    /// <summary>Invariant domain text (never localized), like Porsche's combined entries.</summary>
    internal const string CombinedModelName = "All Models";

    public override Brand Brand => Brand.Daihatsu;

    protected override IReadOnlyList<string> PageUrls => ["https://www.daihatsu.de/"];

    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        HttpDownloadHelper.GetFileName(absoluteUrl).Contains("rettungsdatenbl", StringComparison.OrdinalIgnoreCase);

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        new(ModelName: CombinedModelName, Variant: label, BodyType: null, BuildYearFrom: null, BuildYearTo: null,
            Doors: null, FuelType: null, LanguageCode: "DE", ParseConfidence.Heuristic);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);
        return entries.Select(e => e with { Scope = DocumentScope.Combined }).ToList();
    }
}
