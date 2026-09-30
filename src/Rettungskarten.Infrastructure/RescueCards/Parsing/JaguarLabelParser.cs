using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses jaguar.com's rescue-sheet link texts (see <c>JaguarRescueCardSource</c>): "JAGUAR {model}
/// [{body}] [{drivetrain}] ({from}-[{to}])", e.g. "JAGUAR XF SPORTBRAKE (2017-2020)",
/// "JAGUAR F-PACE PHEV (2021- )", "JAGUAR X-TYPE Saloon (2001-2009)" - and one without any years,
/// "JAGUAR I-PACE", whose year then comes from its filename ("RDB_Jaguar_I-Pace_2018.pdf").
///
/// The model is the first word after the brand (Jaguar's model names are single tokens: XE, XF, XJ,
/// XK, F-TYPE, F-PACE, E-PACE, I-PACE, S-TYPE, X-TYPE), which is also exactly how KBA spells them. The
/// drivetrain is only sometimes in the label ("PHEV"), the mild-hybrid sheets say so only in the
/// filename ("RDB_Jaguar_XE_MHEV_2021.pdf"), so both are searched. The filenames are otherwise
/// free-form and not parsed.
/// </summary>
public static class JaguarLabelParser
{
    private static readonly Regex BrandPrefix = new(@"^JAGUAR\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Jaguar's body-style words, German and English as used on the page and in filenames.</summary>
    public static IReadOnlyList<string> BodyVocabulary { get; } =
        ["Sportbrake", "Cabriolet", "Conv", "Coupé", "Coupe", "Saloon", "Limousine", "Estate"];

    public static ParsedModelInfo Parse(string label, string fileName)
    {
        var text = BrandPrefix.Replace(label.Trim(), string.Empty);
        var fileWords = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');

        var bracket = text.IndexOf('(');
        var namePart = (bracket >= 0 ? text[..bracket] : text).Trim();
        var words = namePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var modelName = words.Length > 0 ? words[0].ToUpperInvariant() : null;

        var years = ModelYearRangeTextHelper.Extract(text, singleYearIsStartYear: true);
        if (years is { From: null, To: null })
        {
            years = ModelYearRangeTextHelper.Extract(fileWords, singleYearIsStartYear: true);
        }

        var body = ToTitle(CanonicalWordMatch.Find(namePart, BodyVocabulary)
            ?? CanonicalWordMatch.Find(fileWords, BodyVocabulary));
        if (string.Equals(body, "Conv", StringComparison.OrdinalIgnoreCase))
        {
            body = "Cabriolet";
        }

        var fuel = CanonicalWordMatch.Find(namePart, VehicleAttributeTextHelper.CommonFuelTypes)
            ?? CanonicalWordMatch.Find(fileWords, VehicleAttributeTextHelper.CommonFuelTypes);
        if (fuel is null && modelName == "I-PACE")
        {
            fuel = "Elektro"; // Jaguar's only battery-electric model; neither label nor filename says so
        }

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: body,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: null,
            FuelType: fuel,
            LanguageCode: "DE",
            ParseConfidence: modelName is not null && years.From is not null ? ParseConfidence.High : ParseConfidence.Unparsed);
    }

    /// <summary>The body style a label or filename names, normalized so the label's "CABRIOLET" and a
    /// filename's "Conv" compare equal (used to catch links whose label and file disagree).</summary>
    public static string? BodyStyleOf(string text)
    {
        var body = CanonicalWordMatch.Find(text.Replace('_', ' '), BodyVocabulary)?.ToLowerInvariant();
        return body switch
        {
            null => null,
            "conv" or "cabriolet" => "cabriolet",
            "coupé" or "coupe" => "coupe",
            "saloon" or "limousine" => "saloon",
            _ => body
        };
    }

    private static string? ToTitle(string? value) =>
        value is null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}
