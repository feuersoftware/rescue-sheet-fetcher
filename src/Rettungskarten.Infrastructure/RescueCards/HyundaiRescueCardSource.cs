using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Hyundai Germany's "Anleitungen und Datenblätter" page: server-rendered HTML with three download
/// lists - "Rettungsdatenblätter", "Leitfaden für Maßnahmen im Notfall" and the AVN
/// radio-navigation manuals - every entry an <c>a.downloadlist__link</c> whose visible label sits in
/// <c>.downloadlist__main-text</c> ("Hyundai KONA Hybrid: Rettungsdatenblatt (ab 04/2023)").
///
/// Verified against the live page (2026-09):
/// - the links point at Adobe Scene7 (<c>dmassets.hyundai.com/is/content/...</c>) and have no
///   <c>.pdf</c> extension at all ("...-rettungsdatenblattpdf"), so candidates are recognized by the
///   label, not the URL - the downloader's %PDF signature check is what validates the file;
/// - the "Maßnahmen im Notfall (inkl. Rettungsdatenblatt)" documents are Hyundai's German Emergency
///   Response Guides (multi-page high-voltage handbooks). Their label contains "Rettungsdatenblatt"
///   too, so they're excluded explicitly - ERGs are out of scope (see
///   <see cref="RescueDocumentClassifier"/>), and the first-generation IONIQ/KONA Elektro they cover
///   also have their one-page sheet in the combined PDF below;
/// - older models (before 11/2019) are only published as one combined 83-page PDF ("Rettungsdatenblätter
///   für Hyundai Modelle vor 11/2019"). It's kept as a single <see cref="DocumentScope.Combined"/>
///   entry and split per model by <c>split hyundai</c> (see <c>HyundaiCombinedPdfLayout</c>).
/// </summary>
public sealed class HyundaiRescueCardSource(IHttpClientFactory httpClientFactory, ILogger<HyundaiRescueCardSource> logger)
    : HtmlPdfLinkRescueCardSource(httpClientFactory, logger)
{
    public const string PageUrl = "https://www.hyundai.com/de/de/service/gut-informiert/anleitungen-und-datenblaetter.html";

    /// <summary>Model name of the combined entry - invariant domain text, like every model name.</summary>
    public const string CombinedModelName = "All Models";

    private static readonly Regex RescueSheetLabel = new(@"Rettungsdatenbl(?:att|ätter|aetter)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmergencyGuideLabel = new(@"Ma(?:ß|ss)nahmen\s+im\s+Notfall", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CombinedLabel = new(@"Rettungsdatenbl\w*\s+für\s+Hyundai\s+Modelle", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override Brand Brand => Brand.Hyundai;

    protected override IReadOnlyList<string> PageUrls => [PageUrl];

    protected override string LinkSelector => "a.downloadlist__link[href]";

    // ~1-17 MB Scene7 files, the combined one the largest.
    protected override string DownloadClientName => Http.RettungskartenHttpClient.LargeDownloadName;

    protected override bool IsCandidateLink(string absoluteUrl, IElement anchor) => RescueSheetLabel.IsMatch(GetLabel(anchor));

    protected override string GetLabel(IElement anchor) =>
        FirstNonEmpty(anchor.QuerySelector(".downloadlist__main-text")?.TextContent, anchor.TextContent) ?? string.Empty;

    protected override bool IsExcluded(string label, string absoluteUrl) =>
        EmergencyGuideLabel.IsMatch(label) || base.IsExcluded(label, absoluteUrl);

    protected override ParsedModelInfo Parse(string label, string absoluteUrl) =>
        IsCombined(label)
            ? new ParsedModelInfo(CombinedModelName, label, null, null, 2019, null, null, "DE", ParseConfidence.Heuristic)
            : HyundaiLabelParser.ParseLabel(label);

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var entries = await base.DiscoverAsync(ct);
        return entries
            .Select(e => e.Parsed.ModelName == CombinedModelName && IsCombined(e.Parsed.Variant ?? string.Empty)
                ? e with { Scope = DocumentScope.Combined }
                : e)
            .ToList();
    }

    private static bool IsCombined(string label) => CombinedLabel.IsMatch(label);
}
