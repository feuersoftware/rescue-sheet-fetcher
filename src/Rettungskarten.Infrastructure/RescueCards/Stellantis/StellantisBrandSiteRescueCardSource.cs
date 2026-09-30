using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>
/// The German brand sites of the former FCA brands - fiat.de (Fiat and, under /professional, Fiat
/// Professional), jeep.de, alfaromeo.de, lancia.de, abarth.de - and the legacy dodge.de site, one
/// instance per <see cref="Brand"/>. All of them publish their rescue sheets ("Rettungsdatenblätter")
/// as a static list of direct PDF links on one server-rendered page (two for Abarth: its brand page and
/// its Mopar service page list different sheets), which is exactly the
/// <see cref="HtmlPdfLinkRescueCardSource"/> shape. What was found on the real pages (2026-09):
///
/// - The page URLs are not uniform: fiat.de and alfaromeo.de redirect "/rettungsdatenblaetter" to
///   "/besitzer/rettungsdatenblaetter" and "/rettungsdaten-blaetter", jeep.de and lancia.de only have
///   it under "/mopar/..." ("/rettungsdatenblaetter" is a 404), Fiat Professional moved to
///   fiat.de/professional/besitzer/rettungsdatenblaetter. The final URLs are used directly, so a moved
///   page fails discovery (the weekly link-check signal) instead of being followed silently.
/// - Every host, including the static 2011-era dodge.de (http only - https doesn't resolve), answers
///   plain requests with 403 (Akamai), so discovery and downloads use the browser-header client.
/// - fiat.de, jeep.de, alfaromeo.de, lancia.de and abarth.de forbid "*.pdf" in robots.txt: the HTTP
///   pipeline reports those downloads as metadata-only. dodge.de has no robots.txt.
/// - Link labels are the link text ("Jeep® Grand Cherokee 4xe (PHEV)", "Tonale Ibrida Plug-in"), except
///   on abarth.de, whose buttons all say "PDF DOWNLOADEN" under an &lt;h2&gt; with the model name, and a
///   few "Hier"/"hier downloaden"/image-only links - for those the heading of the link's own box is
///   used, else the filename (<see cref="GetLabel"/>). Parsing: <see cref="StellantisRescueSheetLabelParser"/>.
/// - lancia.de's labels are unreliable: two different files ("lancia_ypsilon_lpg.pdf",
///   "lancia_ypsilon_2011_lpg.pdf") are both labelled "Lancia Ypsilon 2011 LPG". Its filenames are
///   descriptive and consistent, so Lancia is parsed from the filename instead. Its links are also
///   absolute "http://" URLs to the site's own https host; they're upgraded to https here to save the
///   redirect hop.
/// - Pages link the same PDF several times (Jeep's Wrangler sheet three times, once from an image);
///   the base class de-duplicates by URL, keeping the first - labelled - link.
/// - fiat.de ("Hier finden Sie eine Sammlung von Rettungsdatenblätter diverser Fiat Modelle") and the
///   Abarth Mopar page (the same file linked as "Abarth Punto", "Abarth 500", "Abarth Punto Evo", ...)
///   link a brand-wide "ShedaSoccorso" collection PDF. It is kept as one
///   <see cref="DocumentScope.Combined"/> entry named <see cref="StellantisRescueSheetLabelParser.CollectionModelName"/>;
///   there is no split layout for it because robots.txt forbids fetching it even once to design one.
/// </summary>
public sealed class StellantisBrandSiteRescueCardSource(
    Brand brand, IHttpClientFactory httpClientFactory, ILogger<StellantisBrandSiteRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    private sealed record Site(IReadOnlyList<string> PageUrls, IReadOnlyList<string> BrandPrefixes, bool ParseFileName = false);

    private static readonly Dictionary<Brand, Site> Sites = new()
    {
        [Brand.Fiat] = new(["https://www.fiat.de/besitzer/rettungsdatenblaetter"], ["Fiat"]),
        [Brand.FiatProfessional] = new(
            ["https://www.fiat.de/professional/besitzer/rettungsdatenblaetter"], ["Fiat Professional", "Fiat"]),
        [Brand.Jeep] = new(["https://www.jeep.de/mopar/rettungsdatenblaetter"], ["Jeep"]),
        [Brand.AlfaRomeo] = new(["https://www.alfaromeo.de/rettungsdaten-blaetter"], ["Alfa Romeo"]),
        [Brand.Lancia] = new(["https://www.lancia.de/mopar/rettungsdatenblaetter"], ["Lancia"], ParseFileName: true),
        [Brand.Abarth] = new(
            ["https://www.abarth.de/rettungsdatenblaetter", "https://www.abarth.de/mopar/rettungsdatenblaetter"], ["Abarth"]),
        [Brand.Dodge] = new(["http://www.dodge.de/informationen-fur-rettungskrafte.html"], ["Dodge"])
    };

    /// <summary>Every brand this source has a site for - one registration per brand.</summary>
    public static IReadOnlyCollection<Brand> SupportedBrands => Sites.Keys;

    /// <summary>Link texts that name the action, not the document.</summary>
    private static readonly Regex GenericLinkText = new(
        @"^(?:hier|here|pdf|download|downloaden|herunterladen)(?:\s+(?:downloaden|download|herunterladen))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Site _site = Sites.TryGetValue(brand, out var site)
        ? site
        : throw new ArgumentOutOfRangeException(nameof(brand), brand, null);

    public override Brand Brand => brand;

    protected override IReadOnlyList<string> PageUrls => _site.PageUrls;

    protected override IReadOnlyList<string> BrandPrefixes => _site.BrandPrefixes;

    protected override string DiscoveryClientName => RettungskartenHttpClient.BrowserName;

    protected override string DownloadClientName => RettungskartenHttpClient.BrowserName;

    protected override string GetLabel(IElement anchor)
    {
        if (_site.ParseFileName)
        {
            return string.Empty; // Parse falls back to the filename
        }

        var text = CollapseWhitespace(anchor.TextContent);
        if (text.Length > 0 && !GenericLinkText.IsMatch(text))
        {
            return text;
        }

        // abarth.de: <div class="text"><h2 class="title">Abarth 595</h2><a ...>PDF DOWNLOADEN</a></div>.
        // Only the link's own box (parent, grandparent) is searched - further up is the page heading.
        var heading = anchor.ParentElement?.QuerySelector("h1, h2, h3, h4, h5, h6")
            ?? anchor.ParentElement?.ParentElement?.QuerySelector("h1, h2, h3, h4, h5, h6");
        return heading is null ? string.Empty : CollapseWhitespace(heading.TextContent);
    }

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        StellantisRescueSheetLabelParser.Parse(label, absoluteUrl, BrandPrefixes);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);
        return entries.Select(e => e with
        {
            DownloadUrl = UpgradeToHttps(e.DownloadUrl, e.SourcePageUrl),
            Scope = StellantisRescueSheetLabelParser.IsCollection(HttpDownloadHelper.GetFileName(e.DownloadUrl!))
                ? DocumentScope.Combined
                : DocumentScope.Single
        }).ToList();
    }

    /// <summary>An "http://" link to the same host as an https page (lancia.de) is fetched over https.
    /// A page that is itself http (dodge.de, which has no https) keeps its links as they are.</summary>
    private static string? UpgradeToHttps(string? url, string pageUrl)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var link) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
        {
            return url;
        }

        return link.Scheme == Uri.UriSchemeHttp && page.Scheme == Uri.UriSchemeHttps &&
            string.Equals(link.Host, page.Host, StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(link) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri
            : url;
    }
}
