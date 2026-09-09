using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Abstractions;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Lamborghini publishes a single static page with a handful of direct PDF links: one combined
/// "Emergency Response Guide" (all models) plus one "Rescue Data Sheet" per current model. No
/// language selector exists - the content is English only, like Porsche's (<c>languageCode: "EN"</c>).
/// Filenames have no underscore-delimited schema like VW/Audi/SEAT/Cupra - the model name is simply
/// whatever precedes "Rescue Data Sheet" in the (URL-decoded) filename, so this brand needs no shared
/// filename parser at all.
/// </summary>
public sealed class LamborghiniRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<LamborghiniRescueCardSource> logger) : IRescueCardSource
{
    private const string PageUrl = "https://www.lamborghini.com/en-en/guide-for-emergency-responders";

    // Matches e.g. "REVUELTO_RESCUE_DATA_SHEET_V12_EN.pdf" or "URUS SE RESCUE DATA SHEET_V8.pdf" (both
    // underscore- and space-separated variants appear across the site's own files) - captures
    // everything before the phrase as the model name. The combined "EMERGENCY RESPONSE GUIDE" file
    // never matches this, so it's excluded by construction rather than needing an explicit deny-list.
    private static readonly Regex RescueDataSheetPattern = new(
        @"^(.*?)[\s_]+RESCUE[\s_]DATA[\s_]SHEET", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Brand Brand => Brand.Lamborghini;

    public async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        var html = await client.GetStringAsync(PageUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var entries = new List<RescueCardEntry>();
        foreach (var anchor in document.QuerySelectorAll("a[href$='.pdf']"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absoluteUrl = HttpDownloadHelper.ResolveUrl(PageUrl, href);
            var fileName = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(new Uri(absoluteUrl).AbsolutePath));

            var match = RescueDataSheetPattern.Match(fileName);
            if (!match.Success)
            {
                continue; // the combined "Emergency Response Guide", or anything else not per-model
            }

            var modelName = match.Groups[1].Value.Replace('_', ' ').Trim();
            var parsed = new ParsedModelInfo(
                ModelName: modelName, Variant: null, BodyType: null,
                BuildYearFrom: null, BuildYearTo: null, Doors: null, FuelType: null,
                LanguageCode: "EN", ParseConfidence.High);

            entries.Add(new RescueCardEntry(Brand.Lamborghini, PageUrl, absoluteUrl, modelName, parsed));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Lamborghini_DiscoveredCount", entries.Count));
        return entries;
    }

    public async Task<RescueCardDownloadResult> DownloadAsync(RescueCardEntry entry, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(RettungskartenHttpClient.Name);
        return await HttpDownloadHelper.DownloadPdfAsync(client, entry.DownloadUrl!, ct);
    }
}
