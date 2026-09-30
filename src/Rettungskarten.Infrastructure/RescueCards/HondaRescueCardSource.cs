using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// honda.de's "Download Rettungsdatenblätter" page is server-rendered AEM markup: per model a text
/// component with an <c>&lt;h2&gt;</c> ("Civic", "CR-V P:HEV"), then per sheet a CTA component with
/// the PDF link (link text = variant, e.g. "Civic 5-Türer Diesel") directly followed by a text
/// component "Amtlicher Typ: FK9 / Bauzeitraum: ab 2018 / PDF (284 KB)". All three are siblings in
/// the same column, so the label is assembled by walking siblings from the link's CTA component: the
/// nearest preceding one with a heading, and the following details paragraph.
///
/// The link's own <c>aria-label</c> is useless ("Der Link öffnet ein pdf." on every link, which the
/// base class would otherwise pick first). Many PDFs are served from Honda Austria's DAM folder
/// (/content/dam/local/austria/...) - still Honda's own German-language sheets, linked from the German
/// page. The page's only other PDF (a CO2/consumption info sheet) is excluded by requiring
/// "rettungsdatenblatt" in the filename. See <see cref="HondaLabelParser"/> for the field rules.
/// </summary>
public sealed class HondaRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<HondaRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.honda.de/cars/services/download-rettungsdatenblaetter.html";

    public override Brand Brand => Brand.Honda;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) &&
        HttpDownloadHelper.GetFileName(absoluteUrl).Contains("rettungsdatenblatt", StringComparison.OrdinalIgnoreCase);

    protected override string GetLabel(IElement anchor)
    {
        var linkText = anchor.QuerySelector(".hme-ctas__cta-link-text")?.TextContent ?? anchor.TextContent;
        var component = ColumnChildOf(anchor.Closest(".hmeCtas") ?? anchor);

        string? heading = null;
        for (var sibling = component.PreviousElementSibling; sibling is not null && heading is null; sibling = sibling.PreviousElementSibling)
        {
            heading = sibling.QuerySelector("h2, h3")?.TextContent;
        }

        // The details paragraph uses <br> between its lines, which TextContent drops - read the text
        // nodes one by one so "…/CU3" and "Bauzeitraum" don't run together.
        var details = component.NextElementSibling is { } next && next.QuerySelector("h2, h3") is null &&
                      next.TextContent.Contains("Bauzeitraum", StringComparison.OrdinalIgnoreCase)
            ? string.Join(' ', next.Descendants<IText>().Select(t => t.Data))
            : string.Empty;

        return $"{heading?.Trim() ?? linkText.Trim()} | {linkText.Trim()} | {details}";
    }

    /// <summary>The ancestor of <paramref name="element"/> that sits directly in the page column
    /// (<c>…-parsys</c>). Most models' CTA components are column children themselves, but some
    /// (Civic, CR-V) wrap each one in an extra <c>.hmeContainer</c> - the heading and details
    /// paragraph are siblings of that wrapper, not of the CTA.</summary>
    private static IElement ColumnChildOf(IElement element)
    {
        var current = element;
        while (current.ParentElement is { } parent)
        {
            if (parent.ClassList.Any(c => c.EndsWith("-parsys", StringComparison.Ordinal)))
            {
                return current;
            }

            current = parent;
        }

        return element;
    }

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        HondaLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl));
}
