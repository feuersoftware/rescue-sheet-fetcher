using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// jaguar.com/de-de's "Rettungskarten" page (found via the de-de sitemap, under "Service und
/// Zubehör / Service-Garantien") is static AEM markup with one link per sheet, link text like
/// "JAGUAR XF SPORTBRAKE (2017-2020)", all PDFs under /content/dam/jdx/pdfs/de/. The filenames are
/// free-form ("RDB_Jaguar_XE_MHEV_2021.pdf", "Jaguar_Rettungsdatenblatt_XF_2007-2015.pdf") and only
/// used for what the label doesn't say (see <see cref="JaguarLabelParser"/>).
///
/// Found on the real page: "JAGUAR XK CABRIOLET (2005-2014)" links to
/// "Jaguar_Rettungsdatenblatt_XK_Coupe_2005-2014.pdf" - the same file the "XK COUPÉ (2005-2014)"
/// entry links. Handing that out as the Cabriolet's sheet would be wrong (roof structure and cut
/// zones differ), so a link whose label and filename name different body styles is skipped with a
/// warning. That has to happen in <see cref="IsCandidateLink"/>: the base class de-duplicates by URL
/// right after it, so rejecting the mislabelled first occurrence later would also drop the correctly
/// labelled second one.
/// </summary>
public sealed class JaguarRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<JaguarRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.jaguar.com/de-de/jdx/service-und-zubehor/service-garantien/rettungskarten.html";

    public override Brand Brand => Brand.Jaguar;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor)
    {
        if (!base.IsCandidateLink(absoluteUrl, anchor) ||
            !new Uri(absoluteUrl).AbsolutePath.Contains("/pdfs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var label = LabelText.Collapse(GetLabel(anchor));
        var fileName = HttpDownloadHelper.GetFileName(absoluteUrl);
        var labelBody = JaguarLabelParser.BodyStyleOf(label);
        var fileBody = JaguarLabelParser.BodyStyleOf(fileName);
        if (labelBody is not null && fileBody is not null && labelBody != fileBody)
        {
            Logger.LogWarning("{Message}", Strings.Get("RescueCards_Generic_MislabelledLinkSkipped", Brand, label, fileName));
            return false;
        }

        return true;
    }

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        JaguarLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl));
}
