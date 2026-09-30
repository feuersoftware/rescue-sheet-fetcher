using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// mgmotor.de/owners/rettungskarten is a static page with one <c>.text-cta-block__holder</c> per
/// sheet: a caption paragraph ("MG ZS EV Rettungskarte") followed by a "Herunterladen" button
/// linking the PDF on cdn.mgmotor.eu. The link text is the same for every sheet, so the label is the
/// caption. MG publishes one current sheet per model/drivetrain and no build years on the page - see
/// <see cref="MgLabelParser"/> for the little year information the filenames carry.
///
/// KBA lists today's SAIC-owned MG as "MG ROEWE" (aliased in BrandNames) with the numbered models as
/// bare numbers ("MG ROEWE 4") - model-aliases.json maps the marketing names.
/// </summary>
public sealed class MgRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<MgRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.mgmotor.de/owners/rettungskarten";

    public override Brand Brand => Brand.MG;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) && anchor.Closest(".text-cta-block__holder") is not null;

    protected override string GetLabel(IElement anchor) =>
        anchor.Closest(".text-cta-block__holder")?.QuerySelector("p")?.TextContent ?? base.GetLabel(anchor);

    /// <summary>Found by checking the text layer of every downloaded PDF (2026-09-29): the "MG4
    /// Electric Rettungskarte" link serves MG's English sheet (neither caption nor filename says so,
    /// and there is no German edition of it on the page). Kept, but recorded as "EN".</summary>
    private static readonly HashSet<string> EnglishOnlyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rettungskarte-MG-4-MY-23.pdf"
    };

    protected override ParsedModelInfo Parse(string label, string absoluteUrl)
    {
        var fileName = HttpDownloadHelper.GetFileName(absoluteUrl);
        var parsed = MgLabelParser.Parse(label, fileName);
        return EnglishOnlyFiles.Contains(fileName) ? parsed with { LanguageCode = "EN" } : parsed;
    }

    protected override string LanguageGroupKey(RescueCardEntry entry) => entry.Parsed.Variant ?? entry.RawFileNameOrLabel;
}
