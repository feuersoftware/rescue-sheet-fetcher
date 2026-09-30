using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Reusable source for the most common shape of manufacturer rescue-sheet page: one or more static
/// HTML pages with direct PDF links, one link per sheet. A concrete brand source only states its page
/// URL(s) and, where needed, overrides how links are selected, labelled, filtered and parsed:
///
/// - <see cref="LinkSelector"/>/<see cref="IsCandidateLink"/>: which anchors are rescue-sheet links
///   (default: every <c>a[href]</c> whose path ends in <c>.pdf</c>);
/// - <see cref="GetLabel"/>: the text the metadata is parsed from (default: <c>title</c>, then
///   <c>aria-label</c>, then the link text);
/// - <see cref="IsExcluded"/>: default drops everything <see cref="RescueDocumentClassifier"/> doesn't
///   consider a rescue sheet (ERGs, legends, manuals);
/// - <see cref="Parse"/>: default tries the standard filename convention first and falls back to
///   <see cref="RescueSheetLabelParser"/> on the label.
///
/// Links are de-duplicated by URL (pages commonly link the same PDF twice - list plus teaser), and
/// <see cref="LanguagePreference"/> is applied when a page offers several languages per model. One
/// page failing to load fails discovery for the brand (exception), consistent with every other source:
/// that is exactly the "page moved" signal the weekly link check exists for.
/// </summary>
public abstract class HtmlPdfLinkRescueCardSource(IHttpClientFactory httpClientFactory, ILogger logger)
    : RescueCardSourceBase(httpClientFactory)
{
    protected ILogger Logger => logger;

    protected abstract IReadOnlyList<string> PageUrls { get; }

    protected virtual string LinkSelector => "a[href]";

    /// <summary>Brand name(s) to strip from the front of labels before extracting the model name
    /// (e.g. "Honda", "Land Rover").</summary>
    protected virtual IReadOnlyList<string> BrandPrefixes => [];

    protected virtual string DefaultLanguageCode => "DE";

    protected virtual bool IsCandidateLink(string absoluteUrl, IElement anchor) =>
        HttpDownloadHelper.GetFileName(absoluteUrl).EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    protected virtual string GetLabel(IElement anchor) =>
        FirstNonEmpty(anchor.GetAttribute("title"), anchor.GetAttribute("aria-label"), anchor.TextContent)
        ?? string.Empty;

    protected virtual bool IsExcluded(string label, string absoluteUrl) =>
        !RescueDocumentClassifier.IsRescueSheet(label, HttpDownloadHelper.GetFileName(absoluteUrl));

    protected virtual ParsedModelInfo Parse(string label, string absoluteUrl)
    {
        if (StandardRescueSheetFilenameParser.TryParse(absoluteUrl, out var fromFileName))
        {
            return fromFileName;
        }

        return RescueSheetLabelParser.Parse(
            string.IsNullOrWhiteSpace(label) ? HttpDownloadHelper.GetFileName(absoluteUrl) : label,
            new RescueSheetLabelParser.Options(BrandPrefixes, DefaultLanguageCode));
    }

    /// <summary>Groups entries for <see cref="LanguagePreference"/> - override when a page offers
    /// several languages and the parsed model name alone doesn't identify one variant.</summary>
    protected virtual string LanguageGroupKey(RescueCardEntry entry) =>
        $"{entry.Parsed.ModelName}|{entry.Parsed.BuildYearFrom}|{entry.Parsed.BodyType}|{entry.Parsed.FuelType}";

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var context = BrowsingContext.New(Configuration.Default);

        var entries = new List<RescueCardEntry>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedRawNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pageUrl in PageUrls)
        {
            ct.ThrowIfCancellationRequested();
            var html = await client.GetStringAsync(pageUrl, ct);
            var document = await context.OpenAsync(req => req.Content(html).Address(pageUrl), ct);

            foreach (var anchor in document.QuerySelectorAll(LinkSelector))
            {
                var href = anchor.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string absoluteUrl;
                try
                {
                    absoluteUrl = HttpDownloadHelper.ResolveUrl(pageUrl, href);
                }
                catch (UriFormatException)
                {
                    continue;
                }

                if (!IsCandidateLink(absoluteUrl, anchor) || !seenUrls.Add(absoluteUrl))
                {
                    continue;
                }

                var label = CollapseWhitespace(GetLabel(anchor));
                if (IsExcluded(label, absoluteUrl))
                {
                    continue;
                }

                var parsed = Parse(label, absoluteUrl);
                if (parsed.LanguageCode is null)
                {
                    parsed = parsed with { LanguageCode = DefaultLanguageCode };
                }

                // The filename is the id-hash input (stable across label rewording); two different
                // folders publishing the same filename fall back to the full URL so ids stay unique.
                var rawName = HttpDownloadHelper.GetFileName(absoluteUrl);
                if (!usedRawNames.Add(rawName))
                {
                    rawName = absoluteUrl;
                }

                entries.Add(new RescueCardEntry(Brand, pageUrl, absoluteUrl, rawName, parsed));
            }
        }

        var preferred = LanguagePreference.PreferGermanThenEnglish(entries, LanguageGroupKey, e => e.Parsed.LanguageCode);
        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, preferred.Count));
        return preferred;
    }

    protected static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    protected static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
