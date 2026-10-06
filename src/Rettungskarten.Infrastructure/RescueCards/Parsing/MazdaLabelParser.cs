using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses one entry of mazda.de's "ALLE RETTUNGSKARTEN" list (see <c>MazdaRescueCardSource</c>):
/// the FAQ group title the entry sits under ("Mazda CX-5", "Mazda2 Hybrid") and the entry's own
/// pipe-separated label, e.g. "Mazda CX-5 | KE | 2012 - 2017 | JMZKE******100000",
/// "Mazda2 3-Türer | DE | ab 2007 | JMZDE *****100000", "Mazda3 - Schrägheck | BP | ab 2019" or the
/// odd ones out "Mazda MX-30 (DR) von 2020 bis 2022" (no pipes, chassis code in brackets),
/// "Mazda CX-5 | KF | 2023 ab JMZKF******350000" (year and VIN range in one segment, "ab" after the
/// year) and "Mazda6e - Limousine (SEDAN) | ab 2025 | LVR HDA***** 400001-ZZZZZZ" (no chassis code).
///
/// - Model name: the group title without the "Mazda " prefix ("CX-5", "MX-5"), except for the
///   numbered models whose marketing name is one word ("Mazda2", "Mazda6e") - KBA calls those "2",
///   "6", which model-aliases.json maps. A trailing " Hybrid" in the title is the drivetrain, not the
///   model ("Mazda2 Hybrid" is a Mazda2).
/// - Chassis code: the short all-caps segment (KE, DJ, NC (RHT) -> NC, KBAC3), or the bracketed code
///   in a pipe-less label.
/// - VIN ranges (JMZKE******100000, ...-ZZZZZZ) identify which cars a sheet covers but aren't part of
///   the schema - they are dropped, and a segment mixing a year with a VIN keeps only the year.
/// - Language: most sheets are German; a handful are only offered in English, recognizable by Mazda's
///   document-code token ("rsen-tdecw-a", "_en_") in the filename - "rsde"/"_de"/"deutsch" means
///   German and wins over "rsen" ("rsen-khecw-a_de.pdf" is the German edition of that document).
/// </summary>
public static class MazdaLabelParser
{
    private static readonly Regex DownloadHint = new(@">|PDF\s+herunterladen", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YearThenAb = new(@"\b((?:19|20)\d{2})\s+ab\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChassisSegment = new(@"^(?<code>[A-Z][A-Z0-9]{1,4})(?:\s*\([A-Z]+\))?$", RegexOptions.Compiled);
    private static readonly Regex BracketedChassis = new(@"\((?<code>[A-Z][A-Z0-9]{1,4})\)", RegexOptions.Compiled);
    private static readonly Regex StandaloneYear = new(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex GermanMarker = new(@"(?<![a-z])(?:de|deutsch|rsde)(?![a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishMarker = new(@"(?<![a-z])(?:en|rsen)(?![a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MildHybridBadge = new(@"e-SKYACTIV\s+[GD]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyList<string> BodyVocabulary =
        [.. VehicleAttributeTextHelper.CommonBodyTypes, "Retractable Fastback", "Roadster Coupe", "Faltverdeck"];

    public static ParsedModelInfo Parse(string groupTitle, string label, string fileName)
    {
        var text = DownloadHint.Replace(label, " ");
        text = YearThenAb.Replace(text, "ab $1");
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var segments = text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var description = segments.Count > 0 ? segments[0] : groupTitle;

        string? chassisCode = null;
        var yearTexts = new List<string>();
        foreach (var segment in segments.Skip(1))
        {
            var chassisMatch = ChassisSegment.Match(segment);
            if (chassisCode is null && chassisMatch.Success)
            {
                chassisCode = chassisMatch.Groups["code"].Value;
                continue;
            }

            if (IsVinFragment(segment) && !StandaloneYear.IsMatch(StripVinTokens(segment)))
            {
                continue; // pure VIN range
            }

            yearTexts.Add(StripVinTokens(segment));
        }

        if (segments.Count <= 1)
        {
            // Pipe-less label ("Mazda MX-30 (DR) ab 2022"): everything is in the one segment.
            yearTexts.Add(description);
            var bracketed = BracketedChassis.Match(description);
            chassisCode = bracketed.Success ? bracketed.Groups["code"].Value : null;
        }

        var years = ModelYearRangeTextHelper.Extract(string.Join(" | ", yearTexts), singleYearIsStartYear: true);

        var (modelName, titleFuel) = SplitGroupTitle(groupTitle);
        var fileWords = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ');

        var fuel = MildHybridBadge.IsMatch(description)
            ? "Mild-Hybrid"
            : CanonicalWordMatch.Find(description, VehicleAttributeTextHelper.CommonFuelTypes)
              ?? titleFuel
              ?? CanonicalWordMatch.Find(fileWords, VehicleAttributeTextHelper.CommonFuelTypes);

        var body = CanonicalWordMatch.Find(description, BodyVocabulary);
        if (string.Equals(body, "Faltverdeck", StringComparison.OrdinalIgnoreCase))
        {
            body = "Roadster"; // "MX-5 mit Faltverdeck" = the soft-top roadster
        }

        var variant = segments.Count > 0
            ? string.Join(" | ", new[] { description, chassisCode }.Concat(yearTexts.Where(y => y != description)).Where(s => !string.IsNullOrWhiteSpace(s)))
            : groupTitle;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant,
            BodyType: body,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(description) ?? VehicleAttributeTextHelper.ExtractDoors(fileWords),
            FuelType: fuel,
            LanguageCode: DetectLanguage(fileName),
            ParseConfidence: years.From is not null || years.To is not null ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: chassisCode);
    }

    public static string DetectLanguage(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (GermanMarker.IsMatch(name))
        {
            return "DE";
        }

        return EnglishMarker.IsMatch(name) ? "EN" : "DE";
    }

    private static (string ModelName, string? Fuel) SplitGroupTitle(string groupTitle)
    {
        var title = groupTitle.Trim();
        string? fuel = null;
        if (title.EndsWith(" Hybrid", StringComparison.OrdinalIgnoreCase))
        {
            title = title[..^" Hybrid".Length].TrimEnd();
            fuel = "Hybrid";
        }

        if (title.StartsWith("Mazda ", StringComparison.OrdinalIgnoreCase))
        {
            title = title["Mazda ".Length..].Trim();
        }

        return (title, fuel);
    }

    private static bool IsVinFragment(string segment) =>
        segment.Contains('*') || segment.Contains("ZZZ", StringComparison.Ordinal);

    private static string StripVinTokens(string segment) =>
        string.Join(' ', segment.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.Contains('*') && !t.Contains("ZZZ", StringComparison.Ordinal)));
}
