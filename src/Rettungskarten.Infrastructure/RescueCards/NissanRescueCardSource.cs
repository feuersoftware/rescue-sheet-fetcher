using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Nissan Germany's rescuers page (<c>rescuers_page.html</c>): server-rendered HTML with one accordion
/// per model (<c>h2.accordion-title</c>: "QASHQAI", "NV400 / Interstar") and inside it a table with one
/// row per sheet - "Fahrzeugversion" (chassis code, doors, body: "J12, 5-Türer, SUV"), "Jahr" ("2021 -
/// Heute"), "Motorisierung" ("Hybrid/e-POWER") and a "Download" cell with the PDF link(s).
///
/// Verified against the live page (2026-09):
/// - the structured row is read instead of only the link, because several filenames don't carry the
///   build years ("Nissan_Qashqai_J10.pdf", "Nissan_Qashqai%20e-Power__SUV_2022_5d-_DE.pdf") - see
///   <see cref="NissanRescueTableRowParser"/>;
/// - the e-POWER rows link a "Rettungsleitfaden" (the ERG, "..._DE_ERG.pdf") next to the sheet; ERGs
///   are dropped by <see cref="RescueDocumentClassifier"/>;
/// - one href is relative without a leading slash ("content/dam/..."), resolved against the page;
/// - the last table, "Alle Modelle", links Nissan's combined sheet PDF (06/2019, 73 pages) - kept as one
///   <see cref="DocumentScope.Combined"/> entry and split per model by <c>split nissan</c>
///   (<c>NissanCombinedPdfLayout</c>).
/// Everything is German; the page offers no other language.
/// </summary>
public sealed class NissanRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<NissanRescueCardSource> logger)
    : RescueCardSourceBase(httpClientFactory)
{
    public const string PageUrl = "https://www.nissan.de/rescuers_page.html";

    public const string CombinedModelName = "All Models";

    private const string CombinedRowHeading = "Alle Modelle";

    public override Brand Brand => Brand.Nissan;

    // The combined PDF is ~16 MB.
    protected override string DownloadClientName => RettungskartenHttpClient.LargeDownloadName;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var html = await client.GetStringAsync(PageUrl, ct);
        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html).Address(PageUrl), ct);

        var entries = new List<RescueCardEntry>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sectionHeading = string.Empty;

        // Headings and rows in document order: each row belongs to the accordion heading before it.
        foreach (var element in document.QuerySelectorAll("h2.accordion-title, tr"))
        {
            if (element.LocalName == "h2")
            {
                sectionHeading = LabelText.Collapse(element.TextContent);
                continue;
            }

            var version = LabelText.Collapse(element.QuerySelector("th[scope=row]")?.TextContent ?? string.Empty);
            var links = element.QuerySelectorAll("td a[href]");
            if (links.Length == 0)
            {
                continue; // header row
            }

            var years = LabelText.Collapse(element.QuerySelector("td[data-th=Jahr]")?.TextContent ?? string.Empty);
            var powertrain = LabelText.Collapse(element.QuerySelector("td[data-th=Motorisierung]")?.TextContent ?? string.Empty);

            foreach (var link in links)
            {
                var entry = TryCreateEntry(link, sectionHeading, version, years, powertrain);
                if (entry is null || !seenUrls.Add(entry.DownloadUrl!))
                {
                    continue;
                }

                // The filename is the id-hash input; the same name in two folders falls back to the URL.
                entries.Add(usedRawNames.Add(entry.RawFileNameOrLabel) ? entry : entry with { RawFileNameOrLabel = entry.DownloadUrl! });
            }
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    private RescueCardEntry? TryCreateEntry(IElement link, string sectionHeading, string version, string years, string powertrain)
    {
        string url;
        try
        {
            url = HttpDownloadHelper.ResolveUrl(PageUrl, link.GetAttribute("href")!);
        }
        catch (UriFormatException)
        {
            return null;
        }

        var fileName = HttpDownloadHelper.GetFileName(url);
        var label = LabelText.Collapse(link.TextContent);
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || !RescueDocumentClassifier.IsRescueSheet(label, fileName))
        {
            return null;
        }

        if (version.Equals(CombinedRowHeading, StringComparison.OrdinalIgnoreCase))
        {
            // "Nissan_Rettungsdatenblaetter_06_2019.pdf": every model up to 06/2019.
            var combined = new ParsedModelInfo(CombinedModelName, version, null, null, 2019, null, null, "DE", ParseConfidence.Heuristic);
            return new RescueCardEntry(Brand, PageUrl, url, fileName, combined, DocumentScope.Combined);
        }

        // The row parser only reads what it recognizes (every numeric field comes from a regex match),
        // so an unusual row yields missing fields, never an exception that would cost the brand's
        // whole discovery.
        var parsed = NissanRescueTableRowParser.Parse(sectionHeading, version, years, powertrain, fileName);
        return new RescueCardEntry(Brand, PageUrl, url, fileName, parsed);
    }
}
