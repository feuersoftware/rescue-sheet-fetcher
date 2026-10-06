using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Mitsubishi's rescue cards come from Mitsubishi Austria's own site (mitsubishi-motors.at/services/rettungskarten):
/// a server-rendered page with one card per sheet - an <c>&lt;h2&gt;</c> heading ("Rettungskarte ASX MY23
/// Plug-in Hybrid") and a "Download PDF (3.1 MB)" link into /content/dam/mitsubishi-motors-at/rettungskarten/.
/// The sheets are German and cover the current European range (Space Star, Colt, ASX, Eclipse Cross,
/// Outlander, L200 - verified 2026-10-06: 16 sheets); robots.txt allows everything.
///
/// Why not a German page: mitsubishi-motors.de/kundenservice/rettungskarten links no sheets itself, it
/// only embeds the importer's PressMatrix catalogue (mitsubishi-publikationen.de), whose robots.txt
/// forbids every automated client except Googlebot/Facebot. Mitsubishi's global site has rescue sheets
/// only for Oceania (English, Australian models). The Austrian page is the manufacturer's own source for
/// the same European models - the trade-off is that older generations sold in Germany (Lancer, Pajero,
/// i-MiEV, pre-2023 Colt, ...) aren't on it.
///
/// The download link's text says nothing, so the label is the heading of the link's own card: the
/// nearest ancestor that contains a heading and exactly one PDF link (the page's CSS class names are
/// build hashes, "card__wrapper___l3teV", so they aren't relied on). See <see cref="MitsubishiLabelParser"/>.
/// </summary>
public sealed class MitsubishiRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<MitsubishiRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    public const string PageUrl = "https://www.mitsubishi-motors.at/services/rettungskarten";

    private const string SheetFolder = "/rettungskarten/";

    public override Brand Brand => Brand.Mitsubishi;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        new Uri(absoluteUrl).AbsolutePath.Contains(SheetFolder, StringComparison.OrdinalIgnoreCase);

    protected override string GetLabel(IElement anchor)
    {
        for (var container = anchor.ParentElement; container is not null; container = container.ParentElement)
        {
            var heading = container.QuerySelector("h1, h2, h3, h4");
            if (heading is null)
            {
                continue;
            }

            // The first ancestor with a heading is the link's own card - unless the card has none and
            // this is already the row of all cards, whose first heading belongs to another sheet.
            return container.QuerySelectorAll("a[href]").Count(a => a.GetAttribute("href")!.Contains(".pdf", StringComparison.OrdinalIgnoreCase)) == 1
                ? heading.TextContent
                : string.Empty;
        }

        return string.Empty;
    }

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        MitsubishiLabelParser.Parse(string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(HttpDownloadHelper.GetFileName(absoluteUrl)).Replace('_', ' ')
            : label);
}
