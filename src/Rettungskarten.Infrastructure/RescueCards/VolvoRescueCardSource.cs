using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// volvocars.com's German "Rettungsleitfäden" page is one server-rendered page with a bullet list of
/// direct PDF links per model family ("Volvo C Modelle", "Volvo V Modelle", ...), link text like
/// "Volvo XC60 Typ D 2009-2017" - despite the page's title these are the one-to-few-page rescue
/// sheets, not ERGs. PDFs are on Volvo's Contentstack asset hosts (www.volvocars.com/images/cs/...
/// with a "?branch=prod_alias" query, and azure-eu-assets.contentstack.com for newer uploads).
///
/// volvocars.com is Akamai-fronted and answers plain (non-browser) requests with 403, so both the
/// page and the downloads use the browser-header client. Several generations have more than one
/// sheet with the same type letter and start year (XC60 Typ U 2017 petrol/diesel vs. plug-in hybrid,
/// V60 Typ Z 2019 vs. its mild-hybrid sheet), told apart by the drivetrain word in the label. See
/// <see cref="VolvoLabelParser"/> for the label format.
/// </summary>
public sealed class VolvoRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<VolvoRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private const string PageUrl = "https://www.volvocars.com/de/l/zubehoer-und-services/rettungsleitfaeden/";

    public override Brand Brand => Brand.Volvo;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string DiscoveryClientName => RettungskartenHttpClient.BrowserName;

    protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        VolvoLabelParser.Parse(label, HttpDownloadHelper.GetFileName(absoluteUrl));

    protected override string LanguageGroupKey(RescueCardEntry entry) =>
        $"{base.LanguageGroupKey(entry)}|{entry.Parsed.ChassisCode}";
}
