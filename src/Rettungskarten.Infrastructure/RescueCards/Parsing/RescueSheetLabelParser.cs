using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Generic best-effort parser for free-text rescue-sheet labels ("CAPTUR 2 - 2021", "Spring (ab 2024)",
/// "Astra L Kombi (Sports Tourer) Hybrid (2021)", "Volvo XC60 Typ D 2009-2017") - the fallback for
/// sources whose filenames don't follow the standard convention (see
/// <see cref="StandardRescueSheetFilenameParser"/>). Brand sources with a more regular label format
/// should parse it themselves; this only knows cross-brand conventions:
///
/// - years via <see cref="ModelYearRangeTextHelper"/>, with a single year read as the start year
///   (on labels it's the launch year of a model still being built, not the one year it applies to);
/// - body type/fuel type/doors via the common whole-word vocabularies in
///   <see cref="VehicleAttributeTextHelper"/>;
/// - the model name is the text before the first year, bracket, "|"/","/" - " separator,
///   "ab/bis/from" phrase or vocabulary word, with a leading brand name (<see cref="Options.BrandPrefixes"/>)
///   stripped.
///
/// Confidence is <see cref="ParseConfidence.Heuristic"/> when a model name and a year were found,
/// otherwise <see cref="ParseConfidence.Unparsed"/> - a label without a year usually means the
/// label format wasn't understood, and the card should be looked at rather than trusted.
/// </summary>
public static class RescueSheetLabelParser
{
    public sealed record Options(
        IReadOnlyList<string>? BrandPrefixes = null,
        string LanguageCode = "DE",
        IReadOnlyList<string>? BodyTypes = null,
        IReadOnlyList<string>? FuelTypes = null);

    private static readonly Regex ModelNameTerminator = new(
        @"\s*(?:[(\[|,;]|\s-\s|\s–\s|(?<![\p{L}\p{N}])(?:19|20)\d{2}(?!\d)|\b(?:ab|bis|seit|vor|from|since|until|MJ|Modelljahr)\b|(?<![\p{L}\p{N}])\d{1,2}\s*[./]\s*(?:19|20)\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string label, Options? options = null)
    {
        options ??= new Options();
        var text = Whitespace.Replace(label.Replace('_', ' '), " ").Trim();

        var years = ModelYearRangeTextHelper.Extract(text, singleYearIsStartYear: true);
        var bodyType = VehicleAttributeTextHelper.ExtractLastWordMatch(text, options.BodyTypes ?? VehicleAttributeTextHelper.CommonBodyTypes);
        var fuelType = VehicleAttributeTextHelper.ExtractLastWordMatch(text, options.FuelTypes ?? VehicleAttributeTextHelper.CommonFuelTypes);
        var modelName = ExtractModelName(text, options);

        var confidence = modelName is not null && (years.From is not null || years.To is not null)
            ? ParseConfidence.Heuristic
            : ParseConfidence.Unparsed;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: bodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(text),
            FuelType: fuelType,
            LanguageCode: options.LanguageCode,
            ParseConfidence: confidence);
    }

    internal static string? ExtractModelName(string text, Options options)
    {
        var remaining = StripBrandPrefix(text, options.BrandPrefixes);

        // A terminator at position 0 is part of the model name itself (Peugeot "2008", "3008"), not
        // the end of it - take the first one after it.
        var end = remaining.Length;
        var terminator = ModelNameTerminator.Matches(remaining).FirstOrDefault(m => m.Index > 0);
        if (terminator is not null)
        {
            end = terminator.Index;
        }

        // Any body/fuel vocabulary word ends the model name too - not just the one finally picked as
        // the attribute ("Astra L Kombi (Sports Tourer)": the body type is "Sports Tourer", but the
        // model name still ends before "Kombi").
        var firstWord = new[] { options.BodyTypes ?? VehicleAttributeTextHelper.CommonBodyTypes, options.FuelTypes ?? VehicleAttributeTextHelper.CommonFuelTypes }
            .SelectMany(v => VehicleAttributeTextHelper.WholeWordPattern(v).Matches(remaining))
            .Where(m => m.Index > 0)
            .MinBy(m => m.Index);
        if (firstWord is not null && firstWord.Index < end)
        {
            end = firstWord.Index;
        }

        var name = remaining[..end].Trim(' ', '-', ':', '–', '/');
        return name.Length == 0 ? null : name;
    }

    private static string StripBrandPrefix(string text, IReadOnlyList<string>? prefixes)
    {
        foreach (var prefix in (prefixes ?? []).OrderByDescending(p => p.Length))
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (text.Length == prefix.Length || !char.IsLetterOrDigit(text[prefix.Length])))
            {
                return text[prefix.Length..].TrimStart(' ', '-', ':');
            }
        }

        return text;
    }
}
