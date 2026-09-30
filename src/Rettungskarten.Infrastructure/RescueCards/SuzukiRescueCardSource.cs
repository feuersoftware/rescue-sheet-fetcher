using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Suzuki Deutschland (auto.suzuki.de, a Next.js site) replaced its single combined rescue-card PDF
/// with per-model sheets in 2026 (verified 2026-09-29). The "Dokumente &amp; Hilfe" page is now a
/// three-level tree:
///
/// 1. The overview (<c>/service/dokumente-hilfe</c>) links four category pages as rendered anchors -
///    "Aktuelle Modelle", "Modelle bis 2025", "Modelle bis 2024", "Modelle bis 2020". (Its own
///    "Allgemeine Informationen" PDF is a general rescue guide, not a sheet, and isn't collected.)
/// 2. A category page renders nothing but its title server-side: its model tiles ("Across", "SX4
///    S-Cross", ...) exist only in the Next.js RSC payload (<c>self.__next_f.push</c> script chunks), as
///    an escaped-JSON "threeslide" component with <c>cta_text</c>/<c>cta_link</c> per model. Those two
///    fields are read with a regex on the escaped JSON rather than by reassembling the RSC stream,
///    which is an undocumented framework format; rendered anchors below the category path are
///    accepted too, should Suzuki ever render them.
/// 3. A model page (e.g. <c>.../modelle-bis-2020/swift-dokumente-bis-2020</c>) is server-rendered and
///    lists one link per sheet ("Rettungskarte Swift 3-Türer MY ab 2005", "Rettungskarte S-Cross 1.5
///    DUALJET") to CloudFront-hosted PDFs.
///
/// The model name comes from the category tile (reliable, and what KBA knows), the generation/variant
/// from the link text. The link text often has no year at all ("Rettungskarte Vitara"), so a category
/// named "Modelle bis YYYY" supplies the end year when the label doesn't state one. The Swift page also
/// has empty duplicate anchors (a stray <c>&lt;br&gt;</c> wrapped in a link to the same PDF), so links are
/// grouped by URL and the longest text wins. The same PDF linked from two categories is collected once.
/// A failing category or model page is logged and skipped; only a failing overview fails discovery.
/// </summary>
public sealed class SuzukiRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<SuzukiRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    internal const string OverviewUrl = "https://auto.suzuki.de/service/dokumente-hilfe";
    private const string OverviewPath = "/service/dokumente-hilfe/";

    private static readonly Regex RscModelTile = new(
        @"\\?""cta_text\\?"":\\?""(?<text>(?:[^""\\]|\\u[0-9a-fA-F]{4})*)\\?"",\\?""alt_text\\?"":\\?""(?:[^""\\]|\\u[0-9a-fA-F]{4})*\\?"",\\?""cta_link\\?"":\\?""(?<link>/service/dokumente-hilfe/[^""\\]+)",
        RegexOptions.Compiled);

    private static readonly Regex CategoryEndYear = new(@"bis-((?:19|20)\d{2})(?:/|$)", RegexOptions.Compiled);
    private static readonly Regex LabelNoise = new(@"^\s*Rettungskarte\s*|[↓ ]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public override Brand Brand => Brand.Suzuki;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var context = BrowsingContext.New(Configuration.Default);

        var overviewHtml = await client.GetStringAsync(OverviewUrl, ct);
        var categories = await FindCategoryUrlsAsync(context, overviewHtml, ct);

        var modelPages = new List<(string ModelName, string PageUrl, int? CategoryEndYear)>();
        var seenModelPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var categoryUrl in categories)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await client.GetStringAsync(categoryUrl, ct);
                var endYear = ParseCategoryEndYear(categoryUrl);
                foreach (var (model, url) in await FindModelPagesAsync(context, html, categoryUrl, ct))
                {
                    if (seenModelPages.Add(url))
                    {
                        modelPages.Add((model, url, endYear));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, categoryUrl));
            }
        }

        var entries = new List<RescueCardEntry>();
        var seenPdfs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (model, pageUrl, endYear) in modelPages)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var html = await client.GetStringAsync(pageUrl, ct);
                foreach (var entry in await ParseModelPageAsync(context, html, pageUrl, model, endYear, ct))
                {
                    if (seenPdfs.Add(entry.DownloadUrl!))
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Message}", Strings.Get("RescueCards_Generic_PageReadFailed", Brand, pageUrl));
            }
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    /// <summary>Category pages: rendered links exactly one path segment below the overview.</summary>
    internal static async Task<IReadOnlyList<string>> FindCategoryUrlsAsync(IBrowsingContext context, string html, CancellationToken ct)
    {
        var document = await context.OpenAsync(req => req.Content(html).Address(OverviewUrl), ct);
        return document.QuerySelectorAll("a[href]")
            .Select(a => TryResolve(OverviewUrl, a.GetAttribute("href")!))
            .OfType<Uri>()
            .Where(u => u.Host.Equals("auto.suzuki.de", StringComparison.OrdinalIgnoreCase) &&
                u.AbsolutePath.StartsWith(OverviewPath, StringComparison.OrdinalIgnoreCase) &&
                u.AbsolutePath[OverviewPath.Length..].Trim('/') is { Length: > 0 } rest && !rest.Contains('/'))
            .Select(u => u.GetLeftPart(UriPartial.Path).TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static async Task<IReadOnlyList<(string ModelName, string PageUrl)>> FindModelPagesAsync(
        IBrowsingContext context, string html, string categoryUrl, CancellationToken ct)
    {
        var categoryPath = new Uri(categoryUrl).AbsolutePath.TrimEnd('/') + "/";
        var result = new List<(string, string)>();

        foreach (Match match in RscModelTile.Matches(html))
        {
            var text = Regex.Unescape(match.Groups["text"].Value).Trim();
            var link = match.Groups["link"].Value;
            if (text.Length > 0 && link.StartsWith(categoryPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Add((text, HttpDownloadHelper.ResolveUrl(categoryUrl, link)));
            }
        }

        var document = await context.OpenAsync(req => req.Content(html).Address(categoryUrl), ct);
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var uri = TryResolve(categoryUrl, anchor.GetAttribute("href")!);
            var text = anchor.TextContent.Trim();
            if (uri is not null && text.Length > 0 && uri.AbsolutePath.StartsWith(categoryPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Add((text, uri.AbsoluteUri));
            }
        }

        return result.DistinctBy(r => r.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static async Task<IReadOnlyList<RescueCardEntry>> ParseModelPageAsync(
        IBrowsingContext context, string html, string pageUrl, string modelName, int? categoryEndYear, CancellationToken ct)
    {
        var document = await context.OpenAsync(req => req.Content(html).Address(pageUrl), ct);

        var links = document.QuerySelectorAll("a[href]")
            .Select(a => (Url: TryResolve(pageUrl, a.GetAttribute("href")!), Text: Whitespace.Replace(a.TextContent, " ").Trim()))
            .Where(l => l.Url is not null && l.Url.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.Url!.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Url: g.Key, Text: g.Select(l => l.Text).MaxBy(t => t.Length) ?? string.Empty));

        var entries = new List<RescueCardEntry>();
        foreach (var (url, text) in links)
        {
            var fileName = HttpDownloadHelper.GetFileName(url);
            if (!$"{text} {fileName}".Contains("Rettung", StringComparison.OrdinalIgnoreCase) ||
                !RescueDocumentClassifier.IsRescueSheet(text, fileName))
            {
                continue;
            }

            entries.Add(new RescueCardEntry(Brand.Suzuki, pageUrl, url, fileName, ParseLabel(modelName, text, categoryEndYear)));
        }

        return entries;
    }

    /// <summary>Parses a model page's link text ("Rettungskarte Swift 3-Türer MY ab 2005",
    /// "Rettungskarte Swift Hybrid bis 2020", "Rettungskarte S-Cross 1.5 DUALJET") for the card of
    /// <paramref name="modelName"/> (the category tile's name).</summary>
    internal static ParsedModelInfo ParseLabel(string modelName, string label, int? categoryEndYear)
    {
        var variant = Whitespace.Replace(LabelNoise.Replace(label, " "), " ").Trim();
        var years = ModelYearRangeTextHelper.Extract(variant, singleYearIsStartYear: true);
        if (years is { From: null, To: null } && categoryEndYear is not null)
        {
            years = new YearRange(null, categoryEndYear);
        }

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant.Length > 0 ? variant : null,
            BodyType: VehicleAttributeTextHelper.ExtractLastWordMatch(variant, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(variant),
            FuelType: VehicleAttributeTextHelper.ExtractLastWordMatch(variant, VehicleAttributeTextHelper.CommonFuelTypes),
            LanguageCode: "DE",
            ParseConfidence: ParseConfidence.Heuristic);
    }

    internal static int? ParseCategoryEndYear(string categoryUrl)
    {
        var match = CategoryEndYear.Match(new Uri(categoryUrl).AbsolutePath);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static Uri? TryResolve(string baseUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new Uri(HttpDownloadHelper.ResolveUrl(baseUrl, href));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
