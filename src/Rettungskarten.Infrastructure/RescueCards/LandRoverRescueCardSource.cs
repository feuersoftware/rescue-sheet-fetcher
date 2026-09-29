using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// landrover.de's "Emergency" ownership page is static AEM markup (same platform as Jaguar's) with
/// one link per sheet under /content/dam/lrdx/local/de/pdf-files/. The visible link text is only the
/// year range ("2013 - 2022"); the model is in the link's <c>aria-label</c>, "2013 - 2022:RANGE ROVER
/// SPORT" - which the base class's default label order (title, aria-label, text) already picks. The
/// filenames are free-form ("Range Rover Sport LW_2013-_tcm287-231498.pdf", "RR_Sport_L1_PHEV_2022-.pdf")
/// but carry the generation code. See <see cref="LandRoverLabelParser"/>.
///
/// KBA lists the brand as "LAND ROVER" and the models under their full names ("RANGE ROVER SPORT",
/// "DISCOVERY SPORT"), which the parsed model names match directly.
/// </summary>
public sealed class LandRoverRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<LandRoverRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.landrover.de/ownership/emergency.html";

    public override Brand Brand => Brand.LandRover;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        new Uri(absoluteUrl).AbsolutePath.Contains("/pdf-files/", StringComparison.OrdinalIgnoreCase);

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        LandRoverLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl));

    protected override string LanguageGroupKey(RescueCardEntry entry) =>
        $"{base.LanguageGroupKey(entry)}|{entry.Parsed.Variant}";
}
