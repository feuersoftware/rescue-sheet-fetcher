using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Rettungskarten.Core.Localization;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Mercedes;

/// <summary>One card tile from the rk.mb-qr.com overview, already parsed.</summary>
/// <param name="PortalBrandId">The tile's <c>data-brand</c> value (1 = Mercedes-Benz, 3 = smart,
/// 4 = Mercedes-AMG, 5 = Mercedes-Maybach, 7 = Mercedes-EQ).</param>
/// <param name="CardKey">The detail-page slug the tile links to (e.g. "177.085") - unique per card
/// and stable across runs, unlike the PDF link's cache-busting query string.</param>
public sealed record MercedesPortalCard(
    string PortalBrandId, string CardKey, string DetailPageUrl, string Label, ParsedModelInfo Parsed);

/// <summary>
/// Parses the card grid of Mercedes-Benz' rescue-card portal overview (rk.mb-qr.com/de/, the target
/// of the QR stickers on the vehicles). The page is server-rendered: every card is one
/// <c>ul.allCards &gt; li</c> with its facets as data attributes, and the page's own filter controls
/// carry the id -> name lookup tables for them:
///
/// - <c>data-class</c> -> <c>#carClass li[data-value]</c> ("A-Klasse", "GLC", "smart fortwo", ...),
///   used as the model name;
/// - <c>data-model</c> -> <c>#carBodies li[data-value]</c> ("Limousine", "T-Modell", "Coupé", ...),
///   used as the body type;
/// - <c>data-type-number</c> is the chassis code ("W177", "X290", "453") as-is;
/// - the drivetrain is taken from the pictogram's alt text ("Pictogramm des Antriebs Hybrid Benzin")
///   rather than <c>data-drive-id</c>, whose ids don't match the drivetrain filter reliably (the
///   A 250e plug-in hybrid carries the id of "Diesel / Benzin").
///
/// The link's <c>aria-label</c>, e.g. "Mercedes-Benz A-Klasse 250e Limousine (W177) (ab 2023)", is
/// always "&lt;brand&gt; &lt;class&gt; &lt;engine&gt; &lt;body&gt; (&lt;type no.&gt;) (&lt;years&gt;)"
/// (verified for all 689 cards on 2026-09-29) - the engine designation between class and body
/// becomes <see cref="ParsedModelInfo.Variant"/> and the last parenthetical the year range. The PDF
/// itself is only linked from each card's detail page, so it's resolved at download time (see
/// <see cref="MercedesRescueCardSource"/>). Door counts aren't stated anywhere in the overview and
/// stay null.
/// </summary>
public static class MercedesCardListParser
{
    private const string DrivePictogramPrefix = "Pictogramm des Antriebs ";

    /// <summary>The trailing "(type no.) (years)" pair of every card label.</summary>
    private static readonly Regex TrailingParentheticals = new(
        @"^(?<text>.*?)\s*\((?<type>[^()]*)\)\s*\((?<years>[^()]*)\)\s*$", RegexOptions.Compiled);

    private static readonly string[] LabelBrandPrefixes = ["Mercedes-Benz", "Mercedes-AMG", "Mercedes-Maybach", "smart"];

    /// <summary>
    /// Returns every card on the page. Throws <see cref="InvalidOperationException"/> when the card list
    /// itself is missing (the portal changed its markup - the link-check signal); a single malformed
    /// tile is parsed as far as possible instead of dropped.
    /// </summary>
    public static IReadOnlyList<MercedesPortalCard> Parse(IDocument document, string pageUrl)
    {
        var cardList = document.QuerySelector("ul.allCards")
            ?? throw new InvalidOperationException(Strings.Get("RescueCards_Mercedes_CardListNotFound", pageUrl));

        var classNames = ReadLookup(document, "#carClass");
        var bodyNames = ReadLookup(document, "#carBodies");

        var cards = new List<MercedesPortalCard>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cardList.Children.Where(c => c.LocalName == "li"))
        {
            var anchor = item.QuerySelector("a[href]");
            var href = anchor?.GetAttribute("href");
            if (anchor is null || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var detailUrl = HttpDownloadHelper.ResolveUrl(pageUrl, href);
            var cardKey = HttpDownloadHelper.GetFileName(detailUrl);
            if (!seenKeys.Add(cardKey))
            {
                continue;
            }

            var label = LabelText.Collapse(anchor.GetAttribute("aria-label") ?? anchor.TextContent);
            var className = classNames.GetValueOrDefault(item.GetAttribute("data-class") ?? string.Empty);
            var bodyName = bodyNames.GetValueOrDefault(item.GetAttribute("data-model") ?? string.Empty);
            var drives = item.QuerySelectorAll("img[alt]")
                .Select(img => img.GetAttribute("alt")!)
                .Where(alt => alt.StartsWith(DrivePictogramPrefix, StringComparison.Ordinal))
                .Select(alt => alt[DrivePictogramPrefix.Length..].Trim())
                .Where(d => d.Length > 0)
                .ToList();

            var parsed = ParseLabel(label, className, bodyName, item.GetAttribute("data-type-number"),
                drives.Count > 0 ? string.Join(" / ", drives) : null);

            cards.Add(new MercedesPortalCard(item.GetAttribute("data-brand") ?? string.Empty, cardKey, detailUrl, label, parsed));
        }

        return cards;
    }

    /// <summary>
    /// Splits a card label into model/variant/body/years with the help of the facet names the tile
    /// already carries. Falls back to <see cref="ParseConfidence.Heuristic"/> (never throws) when the
    /// class or body name doesn't appear in the label where expected.
    /// </summary>
    public static ParsedModelInfo ParseLabel(string label, string? className, string? bodyName, string? typeNumber, string? fuelType)
    {
        var match = TrailingParentheticals.Match(label);
        var text = match.Success ? match.Groups["text"].Value : label;
        var years = ModelYearRangeTextHelper.Extract(match.Success ? match.Groups["years"].Value : label, singleYearIsStartYear: true);
        var chassisCode = !string.IsNullOrWhiteSpace(typeNumber) ? typeNumber.Trim()
            : match.Success ? match.Groups["type"].Value.Trim() : null;

        var brandPrefix = LabelBrandPrefixes.FirstOrDefault(p => text.StartsWith(p + " ", StringComparison.OrdinalIgnoreCase));
        var confident = match.Success && className is not null && bodyName is not null;

        // The class name is looked for after the brand prefix first ("Mercedes-Maybach Maybach ..." must
        // not match the prefix's own "Maybach"), then anywhere, because some class names overlap the
        // prefix: "Mercedes-AMG GT 63 ..." belongs to class "AMG GT", "smart fortwo Coupé" to "smart fortwo".
        string rest;
        var classIndex = -1;
        if (className is not null)
        {
            classIndex = text.IndexOf(className, brandPrefix?.Length ?? 0, StringComparison.Ordinal);
            if (classIndex < 0)
            {
                classIndex = text.IndexOf(className, StringComparison.Ordinal);
            }
        }

        if (classIndex >= 0)
        {
            rest = text[(classIndex + className!.Length)..].Trim();
        }
        else
        {
            confident = false;
            rest = brandPrefix is null ? text : text[brandPrefix.Length..].Trim();
        }

        if (bodyName is not null && rest.EndsWith(bodyName, StringComparison.Ordinal))
        {
            rest = rest[..^bodyName.Length].Trim();
        }
        else
        {
            confident = false;
        }

        var modelName = className ?? rest.Split(' ', 2)[0];
        if (modelName.StartsWith("smart ", StringComparison.OrdinalIgnoreCase))
        {
            // "smart fortwo" -> "fortwo": the brand isn't part of the model name (KBA: "SMART FORTWO").
            modelName = modelName["smart ".Length..];
        }
        else if (modelName == "E-Klasse" && bodyName is "Coupé" or "Cabriolet")
        {
            // Mercedes markets these as "E-Klasse Coupé/Cabriolet", and KBA counts them as their own
            // series ("E-KLASSE COUPE", 61k vehicles) apart from the saloon/estate "E-KLASSE" (616k) -
            // naming them after the saloon would put a coupé card into the wrong priority tier.
            modelName = "E-Klasse Coupé";
        }

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: string.IsNullOrWhiteSpace(rest) ? null : rest,
            BodyType: bodyName,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: null,
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: confident ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: string.IsNullOrWhiteSpace(chassisCode) ? null : chassisCode);
    }

    private static Dictionary<string, string> ReadLookup(IDocument document, string listSelector) =>
        document.QuerySelectorAll($"{listSelector} li[data-value]")
            .GroupBy(li => li.GetAttribute("data-value")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => LabelText.Collapse(g.First().TextContent), StringComparer.Ordinal);
}
