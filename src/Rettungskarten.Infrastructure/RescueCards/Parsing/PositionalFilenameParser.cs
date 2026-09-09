using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Core positional-token parser for the shared underscore-delimited rescue-card filename convention:
/// Manufacturer_Model[_Variant...]_BodyType_Year[-Year]_Doors_FuelType_LanguageCode.pdf
/// No source publishes these as separate fields, so this is a best-effort heuristic, not a fixed
/// schema - confidence is downgraded when the doors/year tokens don't match their expected shape, and
/// downgraded further to <see cref="ParseConfidence.Unparsed"/> when *neither* matches, since that
/// means the positional assumption itself likely broke rather than just one optional detail being
/// absent (a wrong body type/fuel type is worse than a missing one for a safety-critical rescue card).
///
/// Internal: brand sources reach this only through their own thin entry point (e.g.
/// <see cref="VwSeatCupraFilenameParser"/>, <see cref="AudiFilenameParser"/>) so each brand's own
/// filename quirks get normalized in exactly one place before tokens land here.
/// </summary>
internal static class PositionalFilenameParser
{
    private static readonly Regex YearPattern = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex YearRangePattern = new(
        @"^((?:19|20)\d{2})-((?:19|20)\d{2})$", RegexOptions.Compiled);
    private static readonly Regex DoorsPattern = new(@"^(\d{1,2})d$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LanguageCodePattern = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);

    public static string[] Tokenize(string fileNameOrUrl)
    {
        var fileName = Path.GetFileNameWithoutExtension(fileNameOrUrl.Split('/', '\\')[^1]);
        return fileName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static ParsedModelInfo Parse(string[] tokens)
    {
        // Need at least Manufacturer + Model + BodyType + Year + Doors + FuelType + Language = 7 tokens.
        if (tokens.Length < 7)
        {
            return new ParsedModelInfo(
                ModelName: tokens.Length > 1 ? tokens[1] : null,
                Variant: null, BodyType: null, BuildYearFrom: null, BuildYearTo: null, Doors: null,
                FuelType: null, LanguageCode: null, ParseConfidence.Unparsed);
        }

        // tokens[0] = manufacturer, tokens[^1] = language, tokens[^2] = fuel type, tokens[^3] = doors,
        // tokens[^4] = year(s), tokens[^5] = body type, tokens[1..^5] = model (+ variant tokens, if any).
        var languageCode = tokens[^1];
        var fuelType = tokens[^2];
        var doorsToken = tokens[^3];
        var yearToken = tokens[^4];
        var bodyType = tokens[^5];
        var modelTokens = tokens[1..^5];

        var yearRangeMatch = YearRangePattern.Match(yearToken);
        var yearMatches = YearPattern.IsMatch(yearToken);
        var doorsMatch = DoorsPattern.Match(doorsToken);
        var languageMatches = LanguageCodePattern.IsMatch(languageCode);

        int? buildYearFrom = yearRangeMatch.Success ? int.Parse(yearRangeMatch.Groups[1].Value)
            : yearMatches ? int.Parse(yearToken)
            : null;
        int? buildYearTo = yearRangeMatch.Success ? int.Parse(yearRangeMatch.Groups[2].Value) : null;

        var confidence = buildYearFrom is null && !doorsMatch.Success
            ? ParseConfidence.Unparsed
            : buildYearFrom is not null && doorsMatch.Success && languageMatches
                ? ParseConfidence.High
                : ParseConfidence.Heuristic;

        var modelName = modelTokens.Length > 0 ? modelTokens[0] : null;
        var variant = modelTokens.Length > 1 ? string.Join(' ', modelTokens[1..]) : null;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant,
            BodyType: bodyType,
            BuildYearFrom: buildYearFrom,
            BuildYearTo: buildYearTo,
            Doors: doorsMatch.Success ? int.Parse(doorsMatch.Groups[1].Value) : null,
            FuelType: fuelType,
            LanguageCode: languageMatches ? languageCode.ToUpperInvariant() : null,
            ParseConfidence: confidence);
    }
}
