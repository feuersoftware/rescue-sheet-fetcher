using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Metadata for one row of Nissan's rescuers-page tables (see <c>NissanRescueCardSource</c>): the
/// row states the version ("J12, 5-Türer, SUV" - chassis code first), the build years ("2021 -
/// Heute", "2007 - 2014") and the powertrain ("Hybrid/e-POWER"), and the linked filename mostly
/// follows the standard convention. The table is the more reliable source for years and chassis
/// code (several filenames carry only the launch year or none at all: "Nissan_Qashqai_J10.pdf"), the
/// filename the more reliable one for the model's own spelling ("X-Trail", "Interstar" - the table
/// headings read "X-TRAIL", "NV400 / Interstar").
/// </summary>
public static class NissanRescueTableRowParser
{
    // Chassis/platform codes as Nissan prints them: J12, FE0, ZE1, D23, ME0M, XFK, EXDD, X82, M9.
    // "L1"/"L2" (NV250 body lengths) look alike but aren't codes.
    private static readonly Regex ChassisCode = new(@"^(?!L\d$)[A-Z][A-Z0-9]{1,4}$", RegexOptions.Compiled);

    // Words a filename's model token carries for a drivetrain variant ("Qashqai e-Power",
    // "Juke Hybrid", "Townstar electric") - moved to the variant so the model is what KBA lists.
    private static readonly Regex VariantSuffix = new(@"\s+(e-Power|Hybrid|electric|Elektro)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedModelInfo Parse(string sectionHeading, string version, string years, string powertrain, string fileName)
    {
        var standard = StandardRescueSheetFilenameParser.Parse(fileName);
        var tokens = PositionalFilenameParser.Tokenize(fileName);

        // The model token is the one after "Nissan" whenever the file follows the convention's start
        // ("Nissan_Qashqai__SUV_...", "Nissan_Qashqai+2_J10.pdf"); otherwise the table heading.
        var modelToken = tokens.Length > 1 && tokens[0].Equals("Nissan", StringComparison.OrdinalIgnoreCase)
            ? tokens[1]
            : sectionHeading.Split('/')[0].Trim();

        string? suffix = null;
        var suffixMatch = VariantSuffix.Match(modelToken);
        if (suffixMatch.Success)
        {
            suffix = suffixMatch.Groups[1].Value;
            modelToken = modelToken[..suffixMatch.Index];
        }

        var yearRange = ModelYearRangeTextHelper.Extract(years, singleYearIsStartYear: true);
        if (yearRange is { From: null, To: null } && standard.BuildYearFrom is not null)
        {
            yearRange = new YearRange(standard.BuildYearFrom, standard.BuildYearTo);
        }

        var body = VehicleAttributeTextHelper.ExtractLastWordMatch(version, VehicleAttributeTextHelper.CommonBodyTypes)
            ?? (standard.ParseConfidence != ParseConfidence.Unparsed ? standard.BodyType : null);

        var variant = string.Join(", ", new[] { suffix, CollapseWhitespace(version) }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new ParsedModelInfo(
            ModelName: modelToken,
            Variant: variant.Length > 0 ? variant : null,
            BodyType: body,
            BuildYearFrom: yearRange.From,
            BuildYearTo: yearRange.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(version) ?? standard.Doors,
            FuelType: string.IsNullOrWhiteSpace(powertrain) ? null : CollapseWhitespace(powertrain),
            LanguageCode: "DE",
            ParseConfidence: yearRange.From is not null ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: ExtractChassisCode(version));
    }

    internal static string? ExtractChassisCode(string version) =>
        version.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => ChassisCode.IsMatch(t) &&
                !VehicleAttributeTextHelper.CommonBodyTypes.Contains(t, StringComparer.OrdinalIgnoreCase));

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
