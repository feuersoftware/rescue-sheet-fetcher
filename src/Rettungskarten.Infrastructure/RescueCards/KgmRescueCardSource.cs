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
    /// Sheets that are English although the German page lists them like every other one - found by
    /// checking the text layer of every downloaded PDF (2026-09-29): all of KGM's newer sheets
    /// (Torres, Actyon J120, Korando e-Motion, Musso EV/Q300) are the English edition, and neither
    /// the page nor the filename says so. KGM offers no German edition of them, so they stay in, but
    /// recorded as "EN". A sheet added later is assumed German until someone checks it.
    /// </summary>
    private static readonly HashSet<string> EnglishOnlyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rettungsdatenblatt-Actyon_J120_2025.pdf",
        "Rettungsdatenblatt-Korando-e-Motion-E100.pdf",
        "Rettungsdatenblatt-Musso_EV_(O100).pdf",
        "Rettungsdatenblatt-Musso_Q300.pdf",
        "Rettungsdatenblatt_Torres_(J100)_ab_09_2022.pdf",
        "Rettungsdatenblatt-Torres EVX_U100.pdf",
        "Rettungsdatenblatt_Torres_HEV_(J140).pdf",
        "Rettungsdatenblatt-Torres EVX_U105.pdf"
    };

    protected override ParsedModelInfo Parse(string label, string absoluteUrl)
    {
        var fileName = HttpDownloadHelper.GetFileName(absoluteUrl);
        var parsed = KgmLabelParser.Parse(label, fileName);
        return EnglishOnlyFiles.Contains(fileName) ? parsed with { LanguageCode = "EN" } : parsed;
    }

    protected override string LanguageGroupKey(RescueCardEntry entry) => entry.RawFileNameOrLabel;
}
