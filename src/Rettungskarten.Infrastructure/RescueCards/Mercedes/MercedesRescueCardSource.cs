using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards.Mercedes;

/// <summary>
/// Mercedes-Benz Group's own rescue-card portal, rk.mb-qr.com - the page the QR stickers on the B
/// pillar/fuel flap of every Mercedes point to. One portal serves five of this tool's brands, told
/// apart by each card tile's <c>data-brand</c>: Mercedes-Benz (1), smart (3, the Mercedes-built
/// fortwo/forfour/roadster generations up to 2024), Mercedes-AMG (4), Mercedes-Maybach (5) and
/// Mercedes-EQ (7). This class is registered once per brand (the brand is a constructor argument);
/// all five instances share the one ~1.4MB overview response through
/// <see cref="DiscoveryResponseCache"/> instead of fetching it five times.
///
/// Found on the real site (2026-09-29): the German overview /de/ is fully server-rendered with all 689
/// cards (passenger cars and vans) and their facets as data attributes - see
/// <see cref="MercedesCardListParser"/> for how the metadata is read. The PDF, however, is only
/// linked from each card's detail page (/de/177.085/ -> /de/card/pdf/481/Mercedes-Benz_A-Klasse_250e_
/// Sedan_2023_4d_Hybrid_DE_177.085v1.7.pdf/?837542, with a changing cache-busting query). Fetching
/// 689 detail pages during discovery would make every dry run (and the weekly link check) take about
/// 12 minutes under the per-host rate limit, so the entry keeps the stable detail-page URL and
/// <see cref="ResolveDownloadUrlAsync"/> follows it only when the PDF is actually downloaded. The
/// sitemap (sitemap-cards.xml) was checked as an alternative, but it lists only the detail pages too.
/// robots.txt allows everything.
///
/// The German list is used, and every detail page under /de/ links the German sheet ("..._DE_...pdf"
/// in every sample checked), so all entries are marked "DE".
/// </summary>
public sealed class MercedesRescueCardSource : RescueCardSourceBase
{
    public const string OverviewUrl = "https://rk.mb-qr.com/de/";

    /// <summary>The portal's <c>data-brand</c> id per brand this source can serve.</summary>
    private static readonly Dictionary<Brand, string> PortalBrandIds = new()
    {
        [Brand.MercedesBenz] = "1",
        [Brand.Smart] = "3",
        [Brand.MercedesAmg] = "4",
        [Brand.Maybach] = "5",
        [Brand.MercedesEq] = "7"
    };

    private readonly string _portalBrandId;
    private readonly DiscoveryResponseCache _responseCache;
    private readonly ILogger<MercedesRescueCardSource> _logger;

    public MercedesRescueCardSource(
        Brand brand, IHttpClientFactory httpClientFactory, DiscoveryResponseCache responseCache, ILogger<MercedesRescueCardSource> logger)
        : base(httpClientFactory)
    {
        if (!PortalBrandIds.TryGetValue(brand, out var portalBrandId))
        {
            throw new ArgumentOutOfRangeException(nameof(brand), brand, "rk.mb-qr.com doesn't serve this brand.");
        }

        Brand = brand;
        _portalBrandId = portalBrandId;
        _responseCache = responseCache;
        _logger = logger;
    }

    public override Brand Brand { get; }

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var html = await _responseCache.GetStringAsync(CreateDiscoveryClient(), OverviewUrl, ct);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(OverviewUrl), ct);

        var entries = MercedesCardListParser.Parse(document, OverviewUrl)
            .Where(card => card.PortalBrandId == _portalBrandId)
            .Select(card => new RescueCardEntry(Brand, OverviewUrl, card.DetailPageUrl, card.CardKey, card.Parsed))
            .ToList();

        _logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    /// <summary>Follows the card's detail page to its PDF link (the print icon); null - a failed
    /// download, not an exception - when the page has none.</summary>
    protected override async Task<string?> ResolveDownloadUrlAsync(RescueCardEntry entry, HttpClient client, CancellationToken ct)
    {
        var detailUrl = entry.DownloadUrl!;
        var html = await client.GetStringAsync(detailUrl, ct);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(detailUrl), ct);

        var href = (document.QuerySelector("a.print-icon[href*='/card/pdf/']") ?? document.QuerySelector("a[href*='/card/pdf/']"))
            ?.GetAttribute("href");
        return string.IsNullOrWhiteSpace(href) ? null : HttpDownloadHelper.ResolveUrl(detailUrl, href);
    }
}
