using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Splitting;

/// <summary>
/// Layout of Subaru Deutschland's combined rescue-card PDF ("SUBARU Rettungskarten für Einsatzkräfte
/// inklusive Autogas bis Modelljahr 2025", 57 pages in 2026; see <see cref="SubaruRescueCardSource"/>).
///
/// The PDF does have bookmarks, but they're unusable as the split basis (verified against the real
/// file): they're left over from an older edition ("... bis MJ 24"), miss every model added since
/// (Impreza/XV/Crosstrek e-BOXER, Forester S5/S6, BRZ ZC facelift/ZD, Solterra - 28 of the 57 pages),
/// and the eight LPG bookmarks point at page 0, i.e. at pages that no longer exist. The page text is
/// reliable instead, in three shapes that occur side by side:
///
/// - Older one-page cards: a header "RETTUNGSKARTE ... SUBARU:  {model} Typ:  {code} Modelljahre:
///   {from} - [{to}]" (PdfPig glues "M3Modelljahre" together - the pattern tolerates that).
/// - Current ISO 17840 cards (4 pages each): every page carries the document id "JF1-T{4 digits}{2
///   letters}" (e.g. "JF1-T4477GG"), which groups them; the first page has "SUBARU {models}
///   {Zwei|Fünf}türige(r) {body} (Typ {code}) {from}-[{to}]".
/// - The Solterra (Toyota-built, 4 pages): only its first page has a header "SUBARU SOLTERRA Typ:
///   {code}ab MJ {year}"; the other three carry nothing identifying, so unkeyed pages continue the
///   preceding group (<see cref="PageTextCombinedPdfLayout.UnkeyedPagesContinuePreviousGroup"/>). That
///   is safe here because every card's first page is keyed; the leading overview/general-notes pages
///   (1-8) come before any keyed page and are therefore dropped.
///
/// Two old cards share an identical header on consecutive pages - "Legacy Typ BL/BP BL/BPS
/// Modelljahre 2004 - 2009" for the saloon and the estate (the body style is only in the drawing) -
/// and so end up as one two-page part for that model generation. Nothing in their text tells them
/// apart, and one part with both cards is correct, just coarser.
/// </summary>
public sealed class SubaruCombinedPdfLayout : PageTextCombinedPdfLayout
{
    private static readonly Regex DocumentId = new(@"JF1-T\d{4}[A-Z]{2}", RegexOptions.Compiled);

    private static readonly Regex OldHeader = new(
        @"SUBARU:\s*(?<model>.+?)\s*Typ:\s*(?<type>.+?)\s*Modelljahre:\s*(?<years>(?:19|20)\d{2}\s*(?:-\s*(?:(?:19|20)\d{2})?)?)",
        RegexOptions.Compiled);

    private static readonly Regex IsoHeader = new(
        @"SUBARU\s+(?<models>[^():]+?)\s+(?<doors>Zwei|Drei|Vier|Fünf)türige[rs]?\s+(?<body>[^\s(]+)\s*\(Typ\s*(?<type>[^)]+)\)\s*(?<years>(?:19|20)\d{2}\s*-\s*(?:(?:19|20)\d{2})?)",
        RegexOptions.Compiled);

    private static readonly Regex TypeHeader = new(
        @"SUBARU\s+(?<model>[A-Za-z][A-Za-z0-9 ]*?)\s+Typ:\s*(?<type>\S+?)\s*ab\s*MJ\s*(?<year>(?:19|20)\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex Hybrid = new(@"\bHYBRID\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FuelWords = new(@"\b(?:HYBRID|Benzin|Diesel|&)\b|&", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public override Brand Brand => Brand.Subaru;

    protected override bool UnkeyedPagesContinuePreviousGroup => true;

    protected override string? TryGetPageKey(string pageText)
    {
        var id = DocumentId.Match(pageText);
        if (id.Success)
        {
            return id.Value;
        }

        var old = OldHeader.Match(pageText);
        if (old.Success)
        {
            return LabelText.Collapse($"{old.Groups["model"].Value} {old.Groups["type"].Value} {old.Groups["years"].Value}");
        }

        var type = TypeHeader.Match(pageText);
        return type.Success ? LabelText.Collapse($"{type.Groups["model"].Value} {type.Groups["type"].Value} ab {type.Groups["year"].Value}") : null;
    }

    protected override ParsedModelInfo ParseGroup(string key, IReadOnlyList<string> pageTexts)
    {
        var first = pageTexts[0];

        var iso = IsoHeader.Match(first);
        if (iso.Success)
        {
            // "Impreza HYBRID SUBARU XV HYBRID": one card for two badge-engineered models - the first
            // one names the part, the full text stays in Variant.
            var models = iso.Groups["models"].Value;
            var firstModel = CleanModelName(models.Split("SUBARU", StringSplitOptions.TrimEntries)[0]);
            var years = ModelYearRangeTextHelper.Extract(iso.Groups["years"].Value);
            return new ParsedModelInfo(
                ModelName: firstModel, Variant: LabelText.Collapse(iso.Value), BodyType: iso.Groups["body"].Value,
                BuildYearFrom: years.From, BuildYearTo: years.To, Doors: DoorWordToCount(iso.Groups["doors"].Value),
                FuelType: Hybrid.IsMatch(models) ? "Hybrid" : null, LanguageCode: "DE", ParseConfidence.Heuristic,
                ChassisCode: iso.Groups["type"].Value.Trim());
        }

        var old = OldHeader.Match(first);
        if (old.Success)
        {
            var model = old.Groups["model"].Value;
            var years = ModelYearRangeTextHelper.Extract(old.Groups["years"].Value);
            return new ParsedModelInfo(
                ModelName: CleanModelName(model.Split('/', StringSplitOptions.TrimEntries)[0]),
                Variant: LabelText.Collapse($"{model} Typ {old.Groups["type"].Value} {old.Groups["years"].Value}"), BodyType: null,
                BuildYearFrom: years.From, BuildYearTo: years.To, Doors: null,
                // "Outback Benzin & Diesel": one card for both engines - no single fuel type.
                FuelType: model.Contains('&') ? null : VehicleAttributeTextHelper.ExtractLastWordMatch(model, VehicleAttributeTextHelper.CommonFuelTypes),
                LanguageCode: "DE", ParseConfidence.Heuristic, ChassisCode: LabelText.Collapse(old.Groups["type"].Value));
        }

        var type = TypeHeader.Match(first);
        if (type.Success)
        {
            var year = int.Parse(type.Groups["year"].Value);
            return new ParsedModelInfo(
                ModelName: ToTitleCase(type.Groups["model"].Value.Trim()), Variant: LabelText.Collapse(type.Value), BodyType: null,
                BuildYearFrom: year, BuildYearTo: null, Doors: null, FuelType: null, LanguageCode: "DE",
                ParseConfidence.Heuristic, ChassisCode: type.Groups["type"].Value);
        }

        // An id-keyed group whose first page lost its header text: keep the id, flag for manual review.
        return new ParsedModelInfo(key, null, null, null, null, null, null, "DE", ParseConfidence.Unparsed);
    }

    private static string CleanModelName(string value) => LabelText.Collapse(FuelWords.Replace(value, " "));

    // "SOLTERRA" is set in capitals on its card only; every other card writes "Solterra"-style names.
    private static string ToTitleCase(string value) =>
        value.Length > 1 && value.All(c => !char.IsLetter(c) || char.IsUpper(c))
            ? char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant()
            : value;

    private static int? DoorWordToCount(string word) => word switch
    {
        "Zwei" => 2,
        "Drei" => 3,
        "Vier" => 4,
        "Fünf" => 5,
        _ => null
    };
}
