using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Tesla's German first-responder landing page (tesla.com/de_DE/firstresponders) only has image
/// cards linking on to the "Fahrzeuge und Laden" sub-page, which is where the documents are:
/// per model (Model S/3/X/Y, Roadster, plus chargers) server-rendered links to PDFs on
/// digitalassets.tesla.com. Each model offers ERGs ("Notfall-Handbuch", "…Emergency_Response_Guide…")
/// next to the rescue sheets ("Notfall-Informationsblatt", "…Rescue_Sheet…") - only the latter are
/// collected, selected by the filename (the labels are the same generic German word for every model).
///
/// The page links several English-only sheets from its German labels (Model S 2021/2022+, Model X
/// 2021/2022+, every Model Y sheet, the Roadster); the filename's language suffix is what's recorded,
/// and those entries are kept as "EN" because Tesla has no German edition of them.
///
/// tesla.com is Akamai-fronted (403 for non-browser requests) - discovery uses the browser-header
/// client. The PDFs on digitalassets.tesla.com are fetched with it too, so both hosts see one
/// consistent client.
/// </summary>
public sealed class TeslaRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<TeslaRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.tesla.com/de_DE/firstresponders/vehicles-charging";

    private static readonly Regex RescueSheetFileName = new(@"rescue[\s_-]*sheet", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Tesla;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string DiscoveryClientName => RettungskartenHttpClient.BrowserName;

    protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        base.IsCandidateLink(absoluteUrl, anchor) && RescueSheetFileName.IsMatch(HttpDownloadHelper.GetFileName(absoluteUrl));

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        TeslaLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl));

    protected override string LanguageGroupKey(RescueCardEntry entry) =>
        $"{entry.Parsed.ModelName}|{entry.Parsed.BuildYearFrom}|{entry.Parsed.BuildYearTo}";
}
