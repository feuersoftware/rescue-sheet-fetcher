using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses the shared underscore-delimited filename convention used by VW, Audi, and Cupra(.ch) rescue
/// card PDFs, e.g. "Volkswagen_Arteon_eHYBRID_Coupe_2020_5d_Hybrid-Electric_DE.pdf" or
/// "Audi_A6__Stationwagon_2018_5d_GD_EN.pdf" (double underscores from an empty variant token are
/// collapsed). The convention is positional from both ends:
/// Manufacturer_Model[_Variant...]_BodyType_Year_Doors_FuelType_LanguageCode.pdf
/// No source publishes these as separate fields, so this is a best-effort heuristic, not a fixed schema -
/// confidence is downgraded when the doors/year tokens don't match their expected shape.
/// </summary>
public static class VwAudiCupraFilenameParser
{
    private static readonly Regex YearPattern = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex DoorsPattern = new(@"^(\d{1,2})d$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LanguageCodePattern = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string fileNameOrUrl)
    {
        var fileName = Path.GetFileNameWithoutExtension(fileNameOrUrl.Split('/', '\\')[^1]);
        var tokens = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Need at least Manufacturer + Model + BodyType + Year + Doors + FuelType + Language = 7 tokens.
        if (tokens.Length < 7)
        {
            return new ParsedModelInfo(
                ModelName: tokens.Length > 1 ? tokens[1] : null,
                Variant: null, BodyType: null, BuildYearFrom: null, BuildYearTo: null, Doors: null,
                FuelType: null, LanguageCode: null, ParseConfidence.Unparsed);
        }

        // tokens[0] = manufacturer, tokens[^1] = language, tokens[^2] = fuel type, tokens[^3] = doors,
        // tokens[^4] = year, tokens[^5] = body type, tokens[1..^5] = model (+ variant tokens, if any).
        var languageCode = tokens[^1];
        var fuelType = tokens[^2];
        var doorsToken = tokens[^3];
        var yearToken = tokens[^4];
        var bodyType = tokens[^5];
        var modelTokens = tokens[1..^5];

        var yearMatches = YearPattern.IsMatch(yearToken);
        var doorsMatch = DoorsPattern.Match(doorsToken);
        var languageMatches = LanguageCodePattern.IsMatch(languageCode);

        var confidence = yearMatches && doorsMatch.Success && languageMatches
            ? ParseConfidence.High
            : ParseConfidence.Heuristic;

        var modelName = modelTokens.Length > 0 ? modelTokens[0] : null;
        var variant = modelTokens.Length > 1 ? string.Join(' ', modelTokens[1..]) : null;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant,
            BodyType: bodyType,
            BuildYearFrom: yearMatches ? int.Parse(yearToken) : null,
            BuildYearTo: null,
            Doors: doorsMatch.Success ? int.Parse(doorsMatch.Groups[1].Value) : null,
            FuelType: fuelType,
            LanguageCode: languageMatches ? languageCode.ToUpperInvariant() : null,
            ParseConfidence: confidence);
    }
}
