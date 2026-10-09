using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses the card headings of Mitsubishi Austria's rescue-card page (see <c>MitsubishiRescueCardSource</c>).
/// Every heading has the shape "Rettungskarte {model} MY{yy} [{drivetrain}] [{cab}]", with the drivetrain
/// sometimes in front of the model year instead: "Rettungskarte Space Star MY20", "Rettungskarte ASX MY23
/// Plug-in Hybrid", "Rettungskarte Outlander PHEV MY19", "Rettungskarte L200 MY20 Klubkabine".
///
/// - The model is the text before "MY" without the drivetrain words; an all-caps name ("COLT") is
///   title-cased, short designations ("ASX", "L200") stay as written. KBA counts the PHEV under the
///   model's own series ("OUTLANDER", "ECLIPSE CROSS").
/// - "MY{yy}" is the model year the sheet starts with (Mitsubishi's "MY20" = 2020); the page states no
///   end year, so it stays open, like a single label year at every other brand.
/// - "Klubkabine"/"Doppelkabine" are the L200's cab styles and become the body type.
/// </summary>
public static class MitsubishiLabelParser
{
    private static readonly Regex DocumentWord = new(@"^\s*Rettungskarte\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ModelYear = new(@"\bMY\s?(?<year>\d{2}|(?:19|20)\d{2})\b", RegexOptions.Compiled);

    private static readonly IReadOnlyList<string> FuelVocabulary = VehicleAttributeTextHelper.CommonFuelTypes;
    private static readonly IReadOnlyList<string> BodyVocabulary = [.. VehicleAttributeTextHelper.CommonBodyTypes, "Klubkabine"];

    public static ParsedModelInfo Parse(string heading)
    {
        var text = LabelText.Collapse(DocumentWord.Replace(heading, string.Empty));

        var yearMatch = ModelYear.Match(text);
        int? year = yearMatch.Success ? ToFourDigitYear(yearMatch.Groups["year"].Value) : null;

        var fuel = CanonicalWordMatch.Find(text, FuelVocabulary);
        var body = CanonicalWordMatch.Find(text, BodyVocabulary);
        var modelName = ModelName(yearMatch.Success ? text[..yearMatch.Index] : text);

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text.Length == 0 ? null : text,
            BodyType: body,
            BuildYearFrom: year,
            BuildYearTo: null,
            Doors: VehicleAttributeTextHelper.ExtractDoors(text),
            FuelType: fuel,
            LanguageCode: "DE",
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed
                : year is not null ? ParseConfidence.High : ParseConfidence.Heuristic);
    }

    private static string? ModelName(string beforeModelYear)
    {
        // Drivetrain/body words can stand between the model and "MY" ("Outlander PHEV MY19").
        var words = beforeModelYear.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => CanonicalWordMatch.Find(w, FuelVocabulary) is null && CanonicalWordMatch.Find(w, BodyVocabulary) is null)
            .Select(w => w.Length >= 4 && w.All(char.IsLetter) && w == w.ToUpperInvariant()
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant())
                : w)
            .ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }

    // "MY20" = 2020; a two-digit year from 50 up is a 1900s model year ("MY99" = 1999), not 2099.
    private static int ToFourDigitYear(string value)
    {
        var year = int.Parse(value, CultureInfo.InvariantCulture);
        return value.Length == 4 ? year : year >= 50 ? 1900 + year : 2000 + year;
    }
}
