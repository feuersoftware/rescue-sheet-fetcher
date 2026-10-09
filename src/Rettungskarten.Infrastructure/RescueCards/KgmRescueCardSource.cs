using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// KGM (SsangYong, renamed 2023; ssangyong.de no longer resolves) publishes its rescue sheets on
/// kgm.de/rettungsdatenblaetter as a plain server-rendered table - "Modell | Modelljahr |
/// Rettungsdatenblatt" - with a PDF link whose text is only "hier". The label is therefore built from
/// the link's table row: "{Modell} | {Modelljahr}", e.g. "Torres EVX | ab 2023". See
/// <see cref="KgmLabelParser"/>.
///
/// Three rows share the model cell "Rexton" (ab 2013/2016/2019 - three generations); the year and
/// the platform code from the filename (Y400, Y415) tell them apart. KBA lists both "KGM" and
/// "SSANGYONG" (both aliased in BrandNames), each with the same model names.
/// </summary>
public sealed class KgmRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<KgmRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.kgm.de/rettungsdatenblaetter";

    public override Brand Brand => Brand.KGM;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) && anchor.Closest("tr") is not null;

    protected override string GetLabel(IElement anchor)
    {
        var cells = anchor.Closest("tr")!.QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList();
        return cells.Count >= 2 ? $"{cells[0]} | {cells[1]}" : base.GetLabel(anchor);
    }

    /// <summary>
    /// Every KGM sheet is the English edition, although the German page lists them as German
    /// "Rettungsdatenblatt" and neither page nor filename says otherwise - checked 2026-10-09 on all 19
    /// (newer ones by their text layer, the older SsangYong ones, which have almost no text layer, by
    /// their English legends and the "Rescue Sheet standard translation (English)" template). KGM
    /// offers no German edition, so they stay in, recorded as "EN".
    /// </summary>
    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        KgmLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl)) with { LanguageCode = "EN" };

    protected override string LanguageGroupKey(RescueCardEntry entry) => entry.RawFileNameOrLabel;
}
