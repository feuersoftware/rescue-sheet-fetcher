using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses volvocars.com's rescue-guide link texts (see <c>VolvoRescueCardSource</c>). Two shapes
/// occur on the real page:
///
/// - the long-standing "Volvo {model} [{drivetrain}] Typ {code} {year}[-{year}] [{drivetrain}]", e.g.
///   "Volvo XC60 Typ D 2009-2017", "Volvo S90 Mild-Hybrid Typ P 2020", "Volvo XC40 Recharge Typ X
///   2021 BEV" (sometimes with underscores left in, "Volvo EX40 Fully Electric_Typ X 2024 BEV");
/// - newer entries whose link text is just the filename in the standard convention
///   ("Volvo_ES90__Hatchback_2027_5d_Electric_DE").
///
/// Both are handled by one word-based parse rather than by <see cref="StandardRescueSheetFilenameParser"/>
/// so the model name comes out the same either way. The model is the leading Volvo designation
/// (XC60, EX30, V90, C30 - one or two letters plus digits); "Typ D" is Volvo's type-approval letter for
/// the generation, kept as <see cref="ParsedModelInfo.ChassisCode"/>. A single year is the start year
/// of a generation still listed without an end ("XC60 Typ U 2017"), and two years separated only by a
/// space (the "S60_Typ_R_2001_2009" filename shape) are a range.
/// </summary>
public static class VolvoLabelParser
{
    private static readonly Regex ModelPattern = new(@"^(?<model>[A-Z]{1,2}\d{2,3})(?![\p{L}\p{N}])", RegexOptions.Compiled);
    private static readonly Regex TypePattern = new(@"\bTyp\s+(?<code>[A-Z0-9]{1,3})\b", RegexOptions.Compiled);
    private static readonly Regex SpaceSeparatedRange = new(@"(?<!\d)((?:19|20)\d{2})\s+((?:19|20)\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex LanguageSuffix = new(@"(?<![\p{L}])(?<lang>DE|EN)$", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string label, string fileName)
    {
        var source = string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(fileName) : label;
        if (source.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^4];
        }

        var text = string.Join(' ', source.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.StartsWith("Volvo ", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Volvo ".Length..];
        }

        var modelMatch = ModelPattern.Match(text);
        var modelName = modelMatch.Success ? modelMatch.Groups["model"].Value : RescueSheetLabelParser.ExtractModelName(text, new RescueSheetLabelParser.Options());

        var typeMatch = TypePattern.Match(text);
        var yearText = SpaceSeparatedRange.Replace(TypePattern.Replace(text, " "), "$1-$2");
        var years = ModelYearRangeTextHelper.Extract(yearText, singleYearIsStartYear: true);

        var languageMatch = LanguageSuffix.Match(Path.GetFileNameWithoutExtension(fileName));
        var language = languageMatch.Success ? languageMatch.Groups["lang"].Value : "DE";

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: CanonicalWordMatch.Find(text, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(text),
            FuelType: CanonicalWordMatch.Find(text, VehicleAttributeTextHelper.CommonFuelTypes),
            LanguageCode: language,
            ParseConfidence: modelMatch.Success && years.From is not null ? ParseConfidence.High
                : modelName is not null && years.From is not null ? ParseConfidence.Heuristic
                : ParseConfidence.Unparsed,
            ChassisCode: typeMatch.Success ? typeMatch.Groups["code"].Value : null);
    }
}
