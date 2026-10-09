using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards.ToyotaGroup;

/// <summary>
/// Toyota Deutschland and Lexus Deutschland run on the same Toyota Europe CMS: one static,
/// server-rendered "Rettungsdatenblätter" page each, every sheet a direct PDF link into Toyota
/// Europe's Scene7 content store (scene7.toyota.eu/is/content/toyotaeurope/...). One class serves both,
/// one instance per brand.
///
/// What the real pages look like (verified 2026-09-29):
/// - every PDF is linked twice (an "opens in a new window" text link and a "Herunterladen" button);
///   the base class de-duplicates by URL. Both carry the same <c>data-gt-label</c> attribute (the
///   analytics label), which is the clean model text - "Auris HV (E15UT 5-Türer, ab 2010)", "Lexus NX
///   450h+ - ab 09/2021" - whereas the visible link text adds "(öffnet ein neues Fenster) .pdf".
/// - Toyota: ~89 sheets back to 2003 (Avensis T25); Lexus: ~24.
/// - both pages also link the general Toyota/Lexus rescue guide ("Rettungsleitfaden", Toyota's in
///   German and English) - a manual, not a per-model sheet, dropped by RescueDocumentClassifier via
///   "Leitfaden".
/// - Toyota's Scene7 paths contain a literal "rettungsdatenblätter" folder; ResolveUrl escapes the
///   umlaut, and Scene7 serves the escaped form.
///
/// All sheets are German (the page is the German market's); file names are stable CMS names and
/// serve as the persisted id.
/// </summary>
public sealed class ToyotaGroupRescueCardSource : HtmlPdfLinkRescueCardSource
{
    private readonly Brand _brand;

    public ToyotaGroupRescueCardSource(Brand brand, IHttpClientFactory httpClientFactory, ILogger<ToyotaGroupRescueCardSource> logger)
        : base(httpClientFactory, logger)
    {
        if (brand is not (Brand.Toyota or Brand.Lexus))
        {
            throw new ArgumentOutOfRangeException(nameof(brand), brand, "Only Toyota and Lexus are served by this source.");
        }

        _brand = brand;
    }

    public override Brand Brand => _brand;

    protected override IReadOnlyList<string> PageUrls => _brand == Brand.Toyota
        ? ["https://www.toyota.de/zubehoer-service/fahrzeuginformationen/rettungsdatenblaetter"]
        : ["https://www.lexus.de/lexus-besitzer/fahrzeuginformationen/rettungsdatenblaetter"];

    protected override string GetLabel(IElement anchor) =>
        FirstNonEmpty(anchor.GetAttribute("data-gt-label"), anchor.GetAttribute("title"), anchor.TextContent) ?? string.Empty;

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        _brand == Brand.Toyota
            ? ToyotaGroupLabelParser.ParseToyota(label, HttpDownloadHelper.GetFileName(absoluteUrl))
            : ToyotaGroupLabelParser.ParseLexus(label);
}
