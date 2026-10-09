using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>
/// Peugeot, Citroën and DS publish their rescue sheets on Stellantis' public "Servicebox" help pages
/// (the brand sites link there), one instance of this source per brand. The site is a static frameset
/// per brand code ("AP" = Peugeot, "AC" = Citroën, "DS"):
/// <c>public.servicebox-parts.com/{code}/secours/{code}/documents/de_DE/index.html</c> loads the menu
/// frame <c>indexLocale.html</c>, whose "Unterstützung und Erste Hilfe" sub-menu
/// (<c>div.sous_menu</c>) links one page per model family (<c>AIDE/14696/FAD_AP_208.html</c>, also
/// irregular names like <c>AIDE/18385/DS_N_8.html</c>, so links are taken by container, not by name).
/// Each model page lists that family's sheets - see <see cref="ServiceboxModelPageParser"/> for its two
/// table shapes, the ERG section that is skipped and the language rule, and
/// <see cref="ServiceboxLabelParser"/> for the metadata. The documented host
/// <c>public.servicebox.com</c> no longer resolves (verified 2026-09-29); <c>servicebox-parts.com</c>
/// serves the same tree. robots.txt only disallows <c>/dtt*</c>.
///
/// Found on the real pages: 26 Peugeot, 17 Citroën and 6 DS model pages (Citroën's C-Elysée page is
/// empty); a model page failing to load is logged and skipped, the menu page failing fails discovery.
/// The same PDF can be linked from two rows (Citroën's C5 "Tourer" link pairs), so entries are
/// de-duplicated by URL.
/// </summary>
public sealed class ServiceboxRescueCardSource : RescueCardSourceBase
{
    private const string Host = "https://public.servicebox-parts.com";

    private readonly ILogger<ServiceboxRescueCardSource> _logger;
    private readonly string _menuUrl;

    public ServiceboxRescueCardSource(
        Brand brand, IHttpClientFactory httpClientFactory, ILogger<ServiceboxRescueCardSource> logger)
        : base(httpClientFactory)
    {
        var code = brand switch
        {
            Brand.Peugeot => "AP",
            Brand.Citroen => "AC",
            Brand.DS => "DS",
            _ => throw new ArgumentOutOfRangeException(nameof(brand), brand, "Servicebox only serves Peugeot, Citroen and DS.")
        };

        Brand = brand;
        _logger = logger;
        _menuUrl = $"{Host}/{code}/secours/{code}/documents/de_DE/indexLocale.html";
    }

    public override Brand Brand { get; }

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var menuHtml = await client.GetStringAsync(_menuUrl, ct);

        var modelPageUrls = new HtmlParser().ParseDocument(menuHtml)
            .QuerySelectorAll("div.sous_menu a[href]")
            .Select(a => a.GetAttribute("href")!.Split('?')[0])
            .Where(href => href.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(href => HttpDownloadHelper.ResolveUrl(_menuUrl, href))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<RescueCardEntry>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pageUrl in modelPageUrls)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ServiceboxSheetLink> links;
            try
            {
                links = ServiceboxModelPageParser.Parse(await client.GetStringAsync(pageUrl, ct), pageUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, pageUrl));
                continue;
            }

            foreach (var link in links)
            {
                var fileName = HttpDownloadHelper.GetFileName(link.PdfUrl);
                if (!seenUrls.Add(link.PdfUrl) || !IsRescueSheet(link.Label, fileName))
                {
                    continue;
                }

                var parsed = ServiceboxLabelParser.Parse(link.Label, fileName, link.FuelIcon, link.LanguageCode);

                // The filename is the stable id-hash input; should two model pages ever publish the
                // same filename in different folders, the full URL keeps the ids unique.
                var rawName = usedRawNames.Add(fileName) ? fileName : link.PdfUrl;
                entries.Add(new RescueCardEntry(Brand, pageUrl, link.PdfUrl, rawName, parsed));
            }
        }

        _logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    /// <summary>A second line of defence behind the page parser's section tracking: ERG files named as
    /// such ("ERG_DS_N8_...") and the French "Manuel de secours" rescue manuals ("Manuel_secours_Ion_...",
    /// "Manuel_C-ZERO_..."), which the shared classifier doesn't know by that word.</summary>
    internal static bool IsRescueSheet(string label, string fileName) =>
        !fileName.StartsWith("Manuel", StringComparison.OrdinalIgnoreCase) &&
        RescueDocumentClassifier.IsRescueSheet(label, fileName);
}
