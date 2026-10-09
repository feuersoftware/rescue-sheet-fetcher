using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// StreetScooter (the former Deutsche Post electric van maker, now B-ON) still publishes its two
/// rescue sheets on the existing-customer support page (streetscooter.com/de/bestandskunden-support/,
/// WordPress): "StreetScooter WORK &amp; WORK L Rettungsdatenblätter" and "StreetScooter WORK XL
/// Rettungsdatenblätter". Each list item's anchor is an empty full-size overlay
/// (<c>a.downloads__link.fill-parent</c>); the title is in the sibling <c>.downloads__title</c>.
///
/// Not a combined document despite the "&amp;": verified against the real PDF, "WORK / WORK L" is one
/// 4-page sheet that covers both lengths of the same van (2014-), so it's a single entry for the WORK
/// with the full title as its variant. The WORK XL filename contains a decomposed "ä" ("a" followed by
/// U+0308) - the link is used exactly as the page writes it, since the server only answers the
/// percent-encoded NFD form (the NFC "%C3%A4" spelling is a 404). StreetScooter isn't in KBA's FZ12
/// (commercial vans below the publication threshold).
/// </summary>
public sealed class StreetscooterRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<StreetscooterRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    internal const string PageUrl = "https://www.streetscooter.com/de/bestandskunden-support/";

    private static readonly Regex Noise = new(@"^\s*StreetScooter\s+|\s+Rettungs(?:daten)?bl(?:a|ä)tt(?:er)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecondVariant = new(@"\s*[&/].*$", RegexOptions.Compiled);

    public override Brand Brand => Brand.Streetscooter;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string LinkSelector => "a.downloads__link[href]";

    protected override string GetLabel(IElement anchor) =>
        FirstNonEmpty(anchor.Closest(".downloads__item")?.QuerySelector(".downloads__title")?.TextContent, anchor.TextContent)
        ?? string.Empty;

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) => ParseLabel(label);

    /// <summary>"StreetScooter WORK &amp; WORK L Rettungsdatenblätter" -> model "WORK", variant "WORK &amp; WORK L".</summary>
    internal static ParsedModelInfo ParseLabel(string label)
    {
        var variant = Noise.Replace(label.Normalize(System.Text.NormalizationForm.FormC), string.Empty).Trim();
        var model = SecondVariant.Replace(variant, string.Empty).Trim();
        return new ParsedModelInfo(
            ModelName: model.Length > 0 ? model : null, Variant: variant.Length > 0 ? variant : null, BodyType: null,
            BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: "Elektro", LanguageCode: "DE",
            ParseConfidence: model.Length > 0 ? ParseConfidence.Heuristic : ParseConfidence.Unparsed);
    }
}
