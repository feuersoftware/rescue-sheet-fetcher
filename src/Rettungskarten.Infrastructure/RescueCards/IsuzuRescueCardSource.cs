using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards;

/// <summary>
/// Isuzu Sales Deutschland's rescue-sheet page (a Pimcore site) groups its PDFs in
/// <c>.download_item--container</c> boxes, one per model generation, each headed by an <c>h2</c> that
/// states the official type approval code and the model years ("D-MAX (Amtlicher Typ: BTF) Ab
/// Modelljahr 2020", "N-SERIE (AMTLICHER TYP: N85, N75) MODELLJAHRE 2009-2010") - the type code is
/// what the page tells owners to compare with their registration document, so it becomes the
/// <see cref="ParsedModelInfo.ChassisCode"/>. The links themselves ("Rettungsdatenblatt D-MAX ab
/// 2020", "Rettungsdatenblatt N-Serie 2009-2010 Einzelkabine") give model, years and cab. The page's
/// last box links a registration-document explainer ("Zulassungsbescheinigung Teil 1") that is not a
/// rescue sheet, so only links naming a "Rettungsdatenblatt" are collected. The heading's years are
/// the fallback when a link states none.
/// </summary>
public sealed class IsuzuRescueCardSource(
    IHttpClientFactory httpClientFactory, ILogger<IsuzuRescueCardSource> logger) : RescueCardSourceBase(httpClientFactory)
{
    internal const string PageUrl = "https://www.isuzu-sales.de/kundenservice/rettungsdatenblaetter/";

    private static readonly Regex TypeCode = new(@"Amtlicher\s+Typ:\s*([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LabelPrefix = new(@"^\s*Rettungsdatenbl(?:a|ä|ae)tt(?:er)?\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly IReadOnlyList<string> BodyTypes = ["Einzelkabine", "Doppelkabine", "Space Cab", "Extended Cab", "Pick-up"];

    public override Brand Brand => Brand.Isuzu;

    public override async Task<IReadOnlyList<RescueCardEntry>> DiscoverAsync(CancellationToken ct)
    {
        var client = CreateDiscoveryClient();
        var html = await client.GetStringAsync(PageUrl, ct);
        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html).Address(PageUrl), ct);

        var entries = new List<RescueCardEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in document.QuerySelectorAll(".download_item--container"))
        {
            var heading = Whitespace.Replace(container.QuerySelector("h2")?.TextContent ?? string.Empty, " ").Trim();
            foreach (var anchor in container.QuerySelectorAll("a[href]"))
            {
                var label = Whitespace.Replace(anchor.TextContent, " ").Trim();
                var url = HttpDownloadHelper.ResolveUrl(PageUrl, anchor.GetAttribute("href")!);
                var fileName = HttpDownloadHelper.GetFileName(url);
                if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                    !$"{label} {fileName}".Contains("Rettungsdatenbl", StringComparison.OrdinalIgnoreCase) ||
                    !RescueDocumentClassifier.IsRescueSheet(label, fileName) || !seen.Add(url))
                {
                    continue;
                }

                entries.Add(new RescueCardEntry(Brand.Isuzu, PageUrl, url, fileName, Parse(label, heading)));
            }
        }

        logger.LogInformation("{Message}", Strings.Get("RescueCards_Generic_DiscoveredCount", Brand, entries.Count));
        return entries;
    }

    internal static ParsedModelInfo Parse(string label, string heading)
    {
        var text = LabelPrefix.Replace(label, string.Empty);
        var parsed = RescueSheetLabelParser.Parse(text, new RescueSheetLabelParser.Options(["Isuzu"], "DE", BodyTypes));

        if (parsed.BuildYearFrom is null && parsed.BuildYearTo is null)
        {
            var headingYears = ModelYearRangeTextHelper.Extract(heading, singleYearIsStartYear: true);
            parsed = parsed with { BuildYearFrom = headingYears.From, BuildYearTo = headingYears.To };
        }

        var typeCode = TypeCode.Match(heading);
        return parsed with
        {
            ChassisCode = typeCode.Success ? typeCode.Groups[1].Value.Trim() : null,
            ParseConfidence = parsed.ModelName is not null ? ParseConfidence.Heuristic : ParseConfidence.Unparsed
        };
    }
}
