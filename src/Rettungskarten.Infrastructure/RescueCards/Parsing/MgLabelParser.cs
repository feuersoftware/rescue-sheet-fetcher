using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses mgmotor.de's rescue-card captions (see <c>MgRescueCardSource</c>): "{model} {drivetrain}
/// [{variant}] Rettungskarte", e.g. "MG3 Hybrid+ Rettungskarte", "MG4 EV MCE (Facelift)
/// Rettungskarte", "MG ZS EV Rettungskarte", "MG Marvel R Electric Rettungskarte", "MGS5 EV
/// Rettungskarte". The captions name no build years; the only year information is in some filenames
/// - a standard-convention one ("MG_4_EV-URBAN_Hatchback_2025_5d_Electric_DE.pdf") or an "MY-23"
/// model-year token ("Rettungskarte-MG-4-MY-23.pdf") - and is used as the start year when present.
///
/// The model is the first word ("MG3", "MGS5", "ZS", "EHS", "Cyberster") - "Marvel R" is the one
/// two-word name. MG's numbered models keep their marketing spelling ("MG4"); KBA's "MG ROEWE 4" etc.
/// are mapped in model-aliases.json.
/// </summary>
public static class MgLabelParser
{
    private static readonly Regex CaptionSuffix = new(@"\s*Rettungskarte\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BrandPrefix = new(@"^MG\s+", RegexOptions.Compiled);
    private static readonly Regex ModelYearToken = new(@"(?i)(?<![a-z])MY[\s_-]*(?<yy>\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex FourDigitYear = new(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string caption, string fileName)
    {
        var text = BrandPrefix.Replace(CaptionSuffix.Replace(caption.Trim(), string.Empty), string.Empty);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? modelName = words.Length == 0 ? null
            : words.Length > 1 && words[0].Equals("Marvel", StringComparison.OrdinalIgnoreCase) && words[1] == "R" ? "Marvel R"
            : words[0];

        var name = Path.GetFileNameWithoutExtension(fileName);
        int? yearFrom = null;
        var modelYear = ModelYearToken.Match(name);
        if (modelYear.Success)
        {
            yearFrom = 2000 + int.Parse(modelYear.Groups["yy"].Value);
        }
        else if (FourDigitYear.Match(name) is { Success: true } year)
        {
            yearFrom = int.Parse(year.Value);
        }

        var fileWords = name.Replace('_', ' ').Replace('-', ' ');

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: CanonicalWordMatch.Find(fileWords, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: yearFrom,
            BuildYearTo: null,
            Doors: VehicleAttributeTextHelper.ExtractDoors(fileWords),
            FuelType: CanonicalWordMatch.Find(text, VehicleAttributeTextHelper.CommonFuelTypes),
            LanguageCode: "DE",
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed : ParseConfidence.Heuristic);
    }
}
