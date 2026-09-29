using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// MAXUS Deutschland's "Rettungsdatenblätter" page (a Next.js site on the Storyblok CMS) renders one
/// tile per model: an anchor to the PDF on Storyblok's asset CDN (a.storyblok.com) whose only text is
/// the model name in an <c>h4</c> ("MAXUS eDELIVER 5", "EV 80", "MAXUS T90 EV"). The sheets state no
/// model years anywhere on the page or in the filenames, so none are set. One tile ("MAXUS eDELIVER 3")
/// links a file called "sicherheitsdatenblatt_..." - it is the same kind of document under the same
/// heading, so any "...datenblatt" PDF counts; other PDFs (price lists) don't. The "e" prefix
/// (eDELIVER, eTERRON) and "EV" mark MAXUS' electric models; the diesel DELIVER 7 has no marker, and
/// no fuel type is guessed for it.
/// </summary>
public sealed class MaxusRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<MaxusRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    internal const string PageUrl = "https://www.maxus.de/de/rettungsdatenblaetter";

    private static readonly Regex Electric = new(@"(?:^|\s)e[A-Z]{3,}|\bEV\b", RegexOptions.Compiled);

    public override Brand Brand => Brand.Maxus;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override IReadOnlyList<string> BrandPrefixes => ["MAXUS"];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        HttpDownloadHelper.GetFileName(absoluteUrl).Contains("datenblatt", StringComparison.OrdinalIgnoreCase);

    protected override string GetLabel(IElement anchor) =>
        FirstNonEmpty(anchor.QuerySelector("h4")?.TextContent, anchor.TextContent) ?? string.Empty;

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) => ParseLabel(label, HttpDownloadHelper.GetFileName(absoluteUrl));

    internal static ParsedModelInfo ParseLabel(string label, string fileName)
    {
        var model = label.StartsWith("MAXUS ", StringComparison.OrdinalIgnoreCase) ? label[6..].Trim() : label.Trim();
        // The body type is only in the (lower-case) filename, e.g. "..._eterron9_pick-up.pdf" - report
        // it in the vocabulary's own spelling.
        var bodyMatch = VehicleAttributeTextHelper.ExtractLastWordMatch(fileName.Replace('_', ' '), VehicleAttributeTextHelper.CommonBodyTypes);
        var bodyType = bodyMatch is null ? null
            : VehicleAttributeTextHelper.CommonBodyTypes.FirstOrDefault(v => v.Equals(bodyMatch, StringComparison.OrdinalIgnoreCase)) ?? bodyMatch;
        return new ParsedModelInfo(
            ModelName: model.Length > 0 ? model : null,
            Variant: label,
            BodyType: bodyType,
            BuildYearFrom: null,
            BuildYearTo: null,
            Doors: null,
            FuelType: Electric.IsMatch(model) ? "Elektro" : null,
            LanguageCode: "DE",
            ParseConfidence: model.Length > 0 ? ParseConfidence.Heuristic : ParseConfidence.Unparsed);
    }
}
