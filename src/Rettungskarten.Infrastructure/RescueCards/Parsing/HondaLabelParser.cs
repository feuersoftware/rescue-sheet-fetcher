using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses the three pieces of text honda.de shows for each rescue sheet (see
/// <c>HondaRescueCardSource</c>): the model heading ("Civic", "CR-V P:HEV"), the link text
/// naming the variant ("Civic 5-Türer Diesel", "JAZZ CROSSTAR e:HEV") and the details paragraph under
/// it ("Amtlicher Typ: FK6/FK7 Bauzeitraum: ab 2017 PDF (153 KB)"). The source passes them joined as
/// "heading | link text | details".
///
/// The details paragraph is the reliable part: "Bauzeitraum" is the build period in the usual
/// "2008 – 2015"/"ab 2017" phrasing, and "Amtlicher Typ" is Honda's type-approval code (CU1/CU2/CU3,
/// FK8, RS5/RS6), kept as <see cref="ParsedModelInfo.ChassisCode"/>. One entry ("Civic Type R ab 2022")
/// has "5dr Hatchback" there instead of a code, so the value is only taken when it looks like one.
/// The filenames ("de_car_rettungsdatenblatt_civic_5d_diesel_2018.pdf") are too inconsistent to trust
/// for years - several carry a publication date ("_2024-06-06", "_2023-11") rather than a build
/// period - and are only used as a fallback for body type, doors and drivetrain.
///
/// Honda's hybrid badges (e:HEV, F:HEV, P:HEV) are normalized to the common German fuel-type
/// vocabulary and stripped from the heading, so "CR-V P:HEV" is model "CR-V" (as KBA counts it).
/// </summary>
public static class HondaLabelParser
{
    private static readonly Regex ChassisPattern = new(@"Amtlicher\s+Typ:\s*(?<code>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChassisCodeShape = new(@"^[A-Z0-9*]{2,}(?:/[A-Z0-9*]+)*$", RegexOptions.Compiled);
    private static readonly Regex BuildPeriodPattern = new(@"Bauzeitraum:\s*(?<period>.*?)\s*(?:PDF\b|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HybridBadge = new(@"(?<![\p{L}\p{N}])[epf]:HEV(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyList<string> FuelVocabulary =
        ["P:HEV", "PHEV", "e:HEV", "EHEV", "F:HEV", "FHEV", "Hybrid", "Diesel"];

    private static readonly IReadOnlyList<string> BodyVocabulary =
        [.. VehicleAttributeTextHelper.CommonBodyTypes, "lim", "tour"];

    public static ParsedModelInfo Parse(string composedLabel, string fileName)
    {
        var parts = composedLabel.Split('|', 3, StringSplitOptions.TrimEntries);
        var heading = parts[0];
        var variant = parts.Length > 1 ? parts[1] : heading;
        var details = parts.Length > 2 ? parts[2] : string.Empty;

        var fileWords = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');

        var modelName = HybridBadge.Replace(heading, string.Empty).Trim();
        if (modelName.Length == 0)
        {
            modelName = variant;
        }

        var periodMatch = BuildPeriodPattern.Match(details);
        var years = periodMatch.Success
            ? ModelYearRangeTextHelper.Extract(periodMatch.Groups["period"].Value, singleYearIsStartYear: true)
            : new YearRange(null, null);

        var chassisMatch = ChassisPattern.Match(details);
        var chassisCode = chassisMatch.Success && ChassisCodeShape.IsMatch(chassisMatch.Groups["code"].Value)
            ? chassisMatch.Groups["code"].Value
            : null;

        var fuel = NormalizeFuel(
            CanonicalWordMatch.Find(variant, FuelVocabulary)
            ?? CanonicalWordMatch.Find(heading, FuelVocabulary)
            ?? CanonicalWordMatch.Find(fileWords, FuelVocabulary));
        if (fuel is null && modelName is "Honda e" or "e:Ny1")
        {
            fuel = "Elektro";
        }

        var body = NormalizeBody(
            CanonicalWordMatch.Find(variant, VehicleAttributeTextHelper.CommonBodyTypes)
            ?? CanonicalWordMatch.Find(fileWords, BodyVocabulary));

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant,
            BodyType: body,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(variant) ?? VehicleAttributeTextHelper.ExtractDoors(fileWords),
            FuelType: fuel,
            LanguageCode: "DE",
            ParseConfidence: years.From is not null || years.To is not null ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: chassisCode);
    }

    private static string? NormalizeFuel(string? badge) => badge?.ToUpperInvariant() switch
    {
        null => null,
        "P:HEV" or "PHEV" => "Plug-in-Hybrid",
        "E:HEV" or "EHEV" or "F:HEV" or "FHEV" or "HYBRID" => "Hybrid",
        _ => "Diesel"
    };

    // The filenames abbreviate "Limousine"/"Tourer" ("accord_lim_2008-2015", "accord_tour_...").
    private static string? NormalizeBody(string? body) => body?.ToLowerInvariant() switch
    {
        null => null,
        "lim" => "Limousine",
        "tour" => "Tourer",
        _ => VehicleAttributeTextHelper.CommonBodyTypes.FirstOrDefault(b => b.Equals(body, StringComparison.OrdinalIgnoreCase)) ?? body
    };
}
