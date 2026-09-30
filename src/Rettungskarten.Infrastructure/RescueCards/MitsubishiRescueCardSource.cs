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
/// Mitsubishi Motors Deutschland's "Rettungskarten" page (mitsubishi-motors.de/kundenservice/rettungskarten)
/// has no links of its own: it embeds, as an iframe, the "Rettungsdatenblätter" category of the
/// importer's PressMatrix publication portal (mitsubishi-publikationen.de, ~40 editions, one per model
/// generation/powertrain, titled e.g. "ASX Plug-in Hybrid Rettungsdatenblatt (ab Modelljahr 2023)").
/// Each edition teaser links an edition page, whose "Download" button is a short link
/// (<c>/d/{code}</c>) that redirects to the PDF on PressMatrix's S3 bucket. The iframe's src is read
/// from the Mitsubishi page rather than hard-coded, so the weekly link check notices when Mitsubishi
/// moves the page or swaps the embedded catalogue.
///
/// The edition page is the entry's stable <c>DownloadUrl</c> and the edition slug its raw name; the
/// <c>/d/</c> link is only looked up at download time (<see cref="ResolveDownloadUrlAsync"/>) - one
/// extra request per card that a dry run doesn't need - and HttpClient follows its redirect to S3.
///
/// Verified 2026-09-29: mitsubishi-publikationen.de's robots.txt disallows every path for every user
/// agent except Googlebot/Facebot ("User-agent: * / Disallow: /"). The HTTP pipeline honours that, so
/// the catalogue request comes back as the pipeline's synthetic "blocked by robots.txt" response. That
/// is a deliberate site policy, not a broken page: discovery reports it as
/// <see cref="NotSupportedException"/> (brand outcome "not implemented", with the reason) instead of a
/// discovery failure that would turn the weekly link check red forever. Everything after that point is
/// implemented and tested against real (trimmed) pages, so the brand works as soon as the portal
/// allows automated access.
/// </summary>
public sealed class MitsubishiRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<MitsubishiRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    private const string PageUrl = "https://www.mitsubishi-motors.de/kundenservice/rettungskarten";

    public override Brand Brand => Brand.Mitsubishi;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var context = BrowsingContext.New(Configuration.Default);

        var pageHtml = await client.GetStringAsync(PageUrl, ct);
        var page = await context.OpenAsync(req => req.Content(pageHtml).Address(PageUrl), ct);

        var catalogueSrc = page.QuerySelectorAll("iframe[src]")
            .Select(f => f.GetAttribute("src")!)
            .FirstOrDefault(src => src.Contains("/editions/category/", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(Strings.Get("RescueCards_Mitsubishi_CatalogueNotFound", PageUrl));
        var catalogueUrl = HttpDownloadHelper.ResolveUrl(PageUrl, catalogueSrc);

        using var response = await client.GetAsync(catalogueUrl, ct);
        if (RobotsTxtDelegatingHandler.IsBlockedResponse(response))
        {
            throw new NotSupportedException(Strings.Get("RescueCards_Mitsubishi_RobotsTxtBlocked", new Uri(catalogueUrl).Host));
        }

        response.EnsureSuccessStatusCode();
        var catalogueHtml = await response.Content.ReadAsStringAsync(ct);
        var entries = await ParseCatalogueAsync(catalogueHtml, catalogueUrl, ct);

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    internal static async Task<IReadOnlyList<RescueCardEntry>> ParseCatalogueAsync(string html, string catalogueUrl, CancellationToken ct)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html).Address(catalogueUrl), ct);

        var entries = new List<RescueCardEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var teaser in document.QuerySelectorAll(".module-teaser"))
        {
            var anchor = teaser.QuerySelector("a[href*='/editions/']");
            var href = anchor?.GetAttribute("href");
            var title = CaptionTitle(teaser.QuerySelector("figcaption"));
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(title) ||
                !title.Contains("Rettungsdatenblatt", StringComparison.OrdinalIgnoreCase) ||
                !RescueDocumentClassifier.IsRescueSheet(title, null))
            {
                continue;
            }

            var editionUrl = HttpDownloadHelper.ResolveUrl(catalogueUrl, href);
            var slug = HttpDownloadHelper.GetFileName(editionUrl);
            if (!seen.Add(slug))
            {
                continue;
            }

            entries.Add(new RescueCardEntry(Brand.Mitsubishi, catalogueUrl, editionUrl, slug, ParseTitle(title)));
        }

        return entries;
    }

    protected override async Task<string?> ResolveDownloadUrlAsync(RescueCardEntry entry, HttpClient client, CancellationToken ct)
    {
        var html = await client.GetStringAsync(entry.DownloadUrl, ct);
        return await FindDownloadLinkAsync(html, entry.DownloadUrl!, ct);
    }

    /// <summary>The edition page's "Download" button: the only link whose path starts with /d/.</summary>
    internal static async Task<string?> FindDownloadLinkAsync(string editionHtml, string editionUrl, CancellationToken ct)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(editionHtml).Address(editionUrl), ct);
        var href = document.QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href")!)
            .FirstOrDefault(h => Uri.TryCreate(new Uri(editionUrl), h, out var u) && u.AbsolutePath.StartsWith("/d/", StringComparison.Ordinal));
        return href is null ? null : HttpDownloadHelper.ResolveUrl(editionUrl, href);
    }

    // The figcaption holds the title, a <br> and the publication date in a <span>; only the text
    // before the <span> is the title.
    private static string? CaptionTitle(IElement? caption)
    {
        if (caption is null)
        {
            return null;
        }

        var text = string.Concat(caption.ChildNodes.TakeWhile(n => n is not IElement { LocalName: "span" }).Select(n => n.TextContent));
        return Whitespace.Replace(text, " ").Trim();
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex DoorsPhrase = new(@"\b(\d)-Türer\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Parenthetical = new(@"\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex FuelPhrase = new(@"\b(?:Plug-in Hybrid|Hybrid|Electric Vehicle)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Body styles Mitsubishi's titles use (L200 cab variants, the two Lancer bodies).
    private static readonly string[] BodyTypes = ["Doppelkabine", "Club Cab", "Einzelkabine", "Sportlimousine", "Sportback"];
    private static readonly Regex BodyPhrase = new(
        @"\b(?:" + string.Join('|', BodyTypes.Select(Regex.Escape)) + @")\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses an edition title like "ASX Plug-in Hybrid Rettungsdatenblatt (ab Modelljahr 2023)",
    /// "L200 Doppelkabine Rettungsdatenblatt (ab Modelljahr 2020)", "Colt 3-Türer Rettungsdatenblatt",
    /// "Plug-in Hybrid Outlander Rettungsdatenblatt (ab Modelljahr 2014)" or
    /// "Electric Vehicle  (i-MiEV) Rettungsdatenblatt": everything before "Rettungsdatenblatt" describes
    /// the vehicle (powertrain, body and door count are cut out of it, whichever side of the model name
    /// they're on), everything after it the model-year range. "2025x" (Mitsubishi's mid-year model
    /// update) counts as 2025. The i-MiEV is only named in brackets behind "Electric Vehicle", so a
    /// bracket is the model name when nothing else is left.
    /// </summary>
    internal static ParsedModelInfo ParseTitle(string title)
    {
        var index = title.IndexOf("Rettungsdatenblatt", StringComparison.OrdinalIgnoreCase);
        var vehicle = index >= 0 ? title[..index] : title;
        var rest = index >= 0 ? title[index..] : string.Empty;

        var years = ModelYearRangeTextHelper.Extract(rest, singleYearIsStartYear: true);

        var fuelMatch = FuelPhrase.Match(vehicle);
        var fuelType = !fuelMatch.Success ? null
            : fuelMatch.Value.Equals("Electric Vehicle", StringComparison.OrdinalIgnoreCase) ? "Elektro"
            : fuelMatch.Value;
        var bodyMatch = BodyPhrase.Match(vehicle);
        var doorsMatch = DoorsPhrase.Match(vehicle);
        var parenthetical = Parenthetical.Match(vehicle);

        var name = Parenthetical.Replace(vehicle, " ");
        name = FuelPhrase.Replace(name, " ");
        name = BodyPhrase.Replace(name, " ");
        name = DoorsPhrase.Replace(name, " ");
        name = Whitespace.Replace(name, " ").Trim();
        if (name.Length == 0 && parenthetical.Success)
        {
            name = parenthetical.Groups[1].Value.Trim();
        }

        return new ParsedModelInfo(
            ModelName: name.Length == 0 ? null : name,
            Variant: Whitespace.Replace(vehicle, " ").Trim(),
            BodyType: bodyMatch.Success ? bodyMatch.Value : null,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: doorsMatch.Success ? int.Parse(doorsMatch.Groups[1].Value) : null,
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: name.Length == 0 ? ParseConfidence.Unparsed : ParseConfidence.Heuristic);
    }
}
