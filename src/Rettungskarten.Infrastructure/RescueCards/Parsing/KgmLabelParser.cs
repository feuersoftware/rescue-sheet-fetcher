using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses one row of kgm.de's rescue-sheet table (see <c>KgmRescueCardSource</c>): the "Modell"
/// cell ("Tivoli X150", "Korando e-Motion", "Torres EVX Cargo Van", "Musso/Musso Grand (Q300)") and
/// the "Modelljahr" cell ("ab 2019"), passed joined as "{model cell} | {year cell}".
///
/// The model is the first word ("Tivoli/XLV" is a Tivoli, "Musso/Musso Grand" a Musso) - except
/// "Actyon Sports", the older pick-up that is a different vehicle from today's "Actyon". KGM's
/// four-character platform codes (X150, C300, J100, Y415, ...) are kept as
/// <see cref="ParsedModelInfo.ChassisCode"/>: from the model cell when it names one, otherwise from
/// the filename ("Rettungsdatenblatt_Torres_(J100)_ab_09_2022.pdf", "...-Rexton-G4_Y415_2019.pdf").
/// "EVX"/"EV"/"e-Motion" mean battery-electric, "Hybrid"/"HEV" a full hybrid.
/// </summary>
public static class KgmLabelParser
{
    private static readonly Regex PlatformCode = new(@"(?<![A-Za-z0-9])(?<code>[A-Z]\d{3})(?![0-9])", RegexOptions.Compiled);
    private static readonly Regex Electric = new(@"(?i)(?<![a-z])(?:EVX|EV|e-Motion)(?![a-z])", RegexOptions.Compiled);
    private static readonly Regex Hybrid = new(@"(?i)(?<![a-z])(?:Hybrid|HEV)(?![a-z])", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string composedLabel, string fileName)
    {
        var parts = composedLabel.Split('|', 2, StringSplitOptions.TrimEntries);
        var modelCell = parts[0];
        var yearCell = parts.Length > 1 ? parts[1] : string.Empty;

        var firstWord = modelCell.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var modelName = modelCell.StartsWith("Actyon Sports", StringComparison.OrdinalIgnoreCase) ? "Actyon Sports" : firstWord;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var codeMatch = PlatformCode.Match(modelCell);
        if (!codeMatch.Success)
        {
            codeMatch = PlatformCode.Match(name);
        }

        var drivetrainText = $"{modelCell} {name.Replace('_', ' ')}";
        var fuel = Electric.IsMatch(drivetrainText) ? "Elektro" : Hybrid.IsMatch(drivetrainText) ? "Hybrid" : null;

        var years = ModelYearRangeTextHelper.Extract(yearCell, singleYearIsStartYear: true);
        var fileWords = name.Replace('_', ' ').Replace('-', ' ');

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: modelCell,
            BodyType: CanonicalWordMatch.Find(modelCell, VehicleAttributeTextHelper.CommonBodyTypes)
                ?? CanonicalWordMatch.Find(name.Replace('_', ' '), VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(fileWords),
            FuelType: fuel,
            LanguageCode: "DE",
            ParseConfidence: modelName is not null && years.From is not null ? ParseConfidence.High : ParseConfidence.Unparsed,
            ChassisCode: codeMatch.Success ? codeMatch.Groups["code"].Value : null);
    }
}
