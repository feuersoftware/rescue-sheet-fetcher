using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Parses Tesla's rescue sheets (see <c>TeslaRescueCardSource</c>). The German first-responder page
/// labels every sheet the same way, "Notfall-Informationsblatt {years}" (once "Rescue Sheet
/// 2022-2025"), so the model only comes from the filename
/// ("2016-2020_Model_S_Rescue_Sheet_de.pdf", "2025-Model-Y-Rescue-Sheet-EN.pdf",
/// "2010-13_Roadster_Rescue_Sheet_en.pdf"), and so does the language: the filename's language suffix
/// is the truth - the page links several English-only sheets from its German labels.
///
/// Years come from the label: "2022+" is an open-ended range, "2016-2020" a closed one, and a bare
/// year ("Notfall-Informationsblatt 2021") means that one model year (the next sheet on the page
/// starts at 2022+).
/// </summary>
public static class TeslaLabelParser
{
    private static readonly Regex ModelPattern = new(@"(?i)(?<![a-z])(?:model[\s_-]*(?<letter>[3sxy])(?![a-z0-9])|(?<roadster>roadster))", RegexOptions.Compiled);
    private static readonly Regex OpenEndedYear = new(@"(?<!\d)((?:19|20)\d{2})\s*\+", RegexOptions.Compiled);
    private static readonly Regex LanguageToken = new(@"(?i)(?<![a-z])(?<lang>de|en)(?![a-z])", RegexOptions.Compiled);

    public static ParsedModelInfo Parse(string label, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var modelMatch = ModelPattern.Match(name);
        var modelName = !modelMatch.Success ? null
            : modelMatch.Groups["roadster"].Success ? "Roadster"
            : $"Model {modelMatch.Groups["letter"].Value.ToUpperInvariant()}";

        var openEnded = OpenEndedYear.Match(label);
        var years = openEnded.Success
            ? new YearRange(int.Parse(openEnded.Groups[1].Value), null)
            : ModelYearRangeTextHelper.Extract(label);

        var languageMatch = LanguageToken.Matches(name).LastOrDefault();
        var language = languageMatch?.Groups["lang"].Value.ToUpperInvariant() ?? "EN";

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: modelName is null ? label : $"{modelName} {label}",
            BodyType: null,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: null,
            FuelType: "Elektro",
            LanguageCode: language,
            ParseConfidence: modelName is not null && years.From is not null ? ParseConfidence.High : ParseConfidence.Unparsed);
    }
}
