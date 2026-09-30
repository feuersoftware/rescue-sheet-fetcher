using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Subaru Deutschland publishes no per-model sheets: the "Rettungskarten" section of its safety page
/// (subaru.de/technik/sicherheit#rettungskarten, a HubSpot CMS page) links one combined PDF for the
/// whole range ("SUBARU Rettungskarten für Einsatzkräfte inklusive Autogas bis Modelljahr 2025", ~41MB,
/// 57 pages in 2026). It is kept as one <see cref="DocumentScope.Combined"/> entry and split per model
/// by <c>split subaru</c> (<see cref="Splitting.SubaruCombinedPdfLayout"/>).
///
/// The same paragraph also carries a second, empty anchor (no text, no image) pointing at the
/// previous edition of that PDF (Dec 2024) - a leftover of the CMS editor, invisible on the page.
/// Only links with visible text are taken, so the stale edition isn't collected next to the current
/// one. A link counts as the rescue-card PDF when its filename or text mentions "Rettung"; the page's
/// other PDFs (brochures) don't.
/// </summary>
public sealed class SubaruRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<SubaruRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    internal const string PageUrl = "https://www.subaru.de/technik/sicherheit";

    /// <summary>Invariant English domain text for the combined entry's model name (never localized).</summary>
    internal const string CombinedModelName = "All Models";

    public override Brand Brand => Brand.Subaru;

    // ~41MB - the default client's 60s total timeout is too short on a slow line.
    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var html = await client.GetStringAsync(PageUrl, ct);
        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html).Address(PageUrl), ct);

        var entries = new List<RescueCardEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var text = anchor.TextContent.Trim();
            string url;
            try
            {
                url = HttpDownloadHelper.ResolveUrl(PageUrl, anchor.GetAttribute("href")!);
            }
            catch (UriFormatException)
            {
                continue;
            }

            var fileName = HttpDownloadHelper.GetFileName(url);
            if (text.Length == 0 || !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                !($"{text} {fileName}".Contains("Rettung", StringComparison.OrdinalIgnoreCase)) ||
                !seen.Add(fileName))
            {
                continue;
            }

            var title = Path.GetFileNameWithoutExtension(fileName);
            var years = ModelYearRangeTextHelper.Extract(title);
            var parsed = new ParsedModelInfo(
                ModelName: CombinedModelName, Variant: title, BodyType: null,
                BuildYearFrom: years.From, BuildYearTo: years.To, Doors: null, FuelType: null,
                LanguageCode: "DE", ParseConfidence.Heuristic);
            entries.Add(new RescueCardEntry(Brand.Subaru, PageUrl, url, fileName, parsed, DocumentScope.Combined));
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }
}
