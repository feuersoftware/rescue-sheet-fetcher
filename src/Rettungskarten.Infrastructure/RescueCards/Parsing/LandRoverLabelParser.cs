using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses landrover.de's rescue-sheet links (see <c>LandRoverRescueCardSource</c>). The visible link
/// text is only the year range; the full description is in the link's <c>aria-label</c> as
/// "{years}:{MODEL [variant] [body] [drivetrain]}", e.g. "2013 - 2022:RANGE ROVER SPORT",
/// "2022:RANGE ROVER SPORT PHEV", "2020:DEFENDER 110 MHEV", "2016:RANGE ROVER EVOQUE CABRIOLET",
/// "1997 - 2006:FREELANDER 3-TÜRER". A single year is the start of a generation still being built.
///
/// The model name is matched against the known model families longest-first ("Range Rover Sport"
/// before "Range Rover") - Land Rover's model names are several words and the variant words after
/// them ("110", "2") don't have a fixed position otherwise. An unknown future model falls back to
/// the text before the first drivetrain/body word. The generation code (LG, LW, L1, LE, ...) is
/// only in the filename ("Range Rover Sport LW_2013-_tcm287-231498.pdf", "RR_Sport_L1_PHEV_2022-.pdf")
/// and is kept as <see cref="ParsedModelInfo.ChassisCode"/> when present.
/// </summary>
public static class LandRoverLabelParser
{
    private static readonly string[] KnownModels =
    [
        "Range Rover Sport", "Range Rover Velar", "Range Rover Evoque", "Range Rover",
        "Discovery Sport", "Discovery", "Defender", "Freelander"
    ];

    private static readonly Regex GenerationCode = new(@"(?<![A-Za-z0-9])(?<code>L[A-Z0-9]{1,2})(?=[_\s-])", RegexOptions.Compiled);

    private static readonly Regex DrivetrainAcronym = new(@"\b(?:Phev|Mhev)\b", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string ariaLabel, string fileName)
    {
        var colon = ariaLabel.IndexOf(':');
        var yearText = colon >= 0 ? ariaLabel[..colon] : ariaLabel;
        var description = (colon >= 0 ? ariaLabel[(colon + 1)..] : ariaLabel).Trim();
        // Title-cased for readability ("Range Rover Sport"), drivetrain acronyms kept upper-case.
        var title = DrivetrainAcronym.Replace(
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(description.ToLowerInvariant()), m => m.Value.ToUpperInvariant());

        var years = ModelYearRangeTextHelper.Extract(yearText, singleYearIsStartYear: true);

        var modelName = KnownModels.FirstOrDefault(m =>
                title.StartsWith(m, StringComparison.OrdinalIgnoreCase) &&
                (title.Length == m.Length || !char.IsLetterOrDigit(title[m.Length])))
            ?? RescueSheetLabelParser.ExtractModelName(title, new RescueSheetLabelParser.Options());

        var codeMatch = GenerationCode.Match(Path.GetFileNameWithoutExtension(fileName));

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: title,
            BodyType: CanonicalWordMatch.Find(title, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(title),
            FuelType: CanonicalWordMatch.Find(description, VehicleAttributeTextHelper.CommonFuelTypes),
            LanguageCode: "DE",
            ParseConfidence: modelName is not null && years.From is not null ? ParseConfidence.High : ParseConfidence.Unparsed,
            ChassisCode: codeMatch.Success ? codeMatch.Groups["code"].Value : null);
    }
}
