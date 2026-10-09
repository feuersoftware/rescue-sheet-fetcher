using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Kia Germany's "Rettungsblätter" page: server-rendered HTML with one teaser card per model - an
/// <c>h3.tit</c> heading ("Kia Sportage Plug-in Hybrid") and a "Herunterladen" button linking the PDF
/// directly - followed by a block of English cards for models Kia also publishes in English, and a
/// "Kia Rettungsdatenblätter für ältere Modelle" block with one combined PDF.
///
/// Verified against the live page (2026-09):
/// - the button text carries no information, so the label is the card's heading, found via the
///   enclosing <c>.cont_area</c>; see <see cref="KiaRescueSheetParser"/> for why the filename is only
///   searched for attributes, not parsed positionally;
/// - one link ends in a stray dot ("rdb_kia_sportage_2020.pdf."), so candidates are ".pdf" links with
///   an optional trailing dot (case-insensitive - ".PDF" occurs too);
/// - the English block repeats models that also have a German card (K4, PV5) - removed by the
///   language preference, which groups by the heading so that "Kia PV5 Cargo" (DE) and "PV5 Cargo"
///   (EN) are recognized as the same sheet; English-only sheets (Picanto, EV6 GT) are kept as "EN";
/// - "ältere Modelle" is one 58-page PDF (models up to 08/2020) kept as a single
///   <see cref="DocumentScope.Combined"/> entry and split per model by <c>split kia</c>
///   (<c>KiaCombinedPdfLayout</c>).
/// </summary>
public sealed class KiaRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<KiaRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    public const string PageUrl = "https://www.kia.com/de/service/service-und-wartung/rettungsblaetter/";

    public const string CombinedModelName = "All Models";

    private static readonly Regex PdfPath = new(@"\.pdf\.?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CombinedHeading = new(@"ältere\s+Modelle", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishMarker = new(@"\[EN\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Kia;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        PdfPath.IsMatch(HttpDownloadHelper.GetFileName(absoluteUrl));

    /// <summary>The card's heading, plus " [EN]" when the button marks the file as English.</summary>
    protected override string GetLabel(IElement anchor)
    {
        var heading = anchor.Closest(".cont_area")?.QuerySelector("h3")?.TextContent
            ?? anchor.Closest(".eut_dt1")?.QuerySelector("h2")?.TextContent
            ?? anchor.TextContent;
        return EnglishMarker.IsMatch(anchor.TextContent) ? $"{heading.Trim()} [EN]" : heading;
    }

    protected override ParsedModelInfo Parse(string label, string absoluteUrl)
    {
        if (CombinedHeading.IsMatch(label))
        {
            // "Kia_Rettungsdatenblaetter_08_2020+Sorento_GER.pdf": models up to 08/2020.
            return new ParsedModelInfo(CombinedModelName, LabelText.Collapse(label), null, null, 2020, null, null, "DE", ParseConfidence.Heuristic);
        }

        var heading = EnglishMarker.Replace(label, string.Empty).Trim();
        return KiaRescueSheetParser.Parse(heading, HttpDownloadHelper.GetFileName(absoluteUrl), EnglishMarker.IsMatch(label) ? "[EN]" : null);
    }

    protected override string LanguageGroupKey(RescueCardEntry entry) =>
        $"{ModelNameNormalizer.Normalize(entry.Parsed.Variant ?? entry.Parsed.ModelName ?? string.Empty)}|{entry.Parsed.BuildYearFrom}";

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);
        return entries
            .Select(e => e.Parsed.ModelName == CombinedModelName ? e with { Scope = DocumentScope.Combined } : e)
            .ToList();
    }
}
