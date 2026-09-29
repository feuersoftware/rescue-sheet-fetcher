using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.IfzBerlin;

/// <summary>
/// Parses one IFZ Berlin rescue-sheet record: its <c>auto_typ</c> (the model group the portal sorts
/// by, e.g. "Astra_L", "Insignia-B", "Crossland X", "Movano_e", "Opel_GT", "Modell_9-3") and its
/// <c>detail_text</c> label, e.g. "Astra L Kombi (Sports Tourer) Hybrid (2021)",
/// "Nubira  4-Türer  Autogas (LPG) (2004)", "Insignia 4-Türer (2008/2013)", "Movano '98 (1998)".
///
/// The model name comes from <c>auto_typ</c>, not the label: it is the portal's own structured
/// grouping and never carries body/fuel words, while labels vary in spelling ("Corsa_F", "Mokka_B",
/// "A s t r a - H 4-porte"). A trailing single capital letter is Opel's generation letter and goes to
/// the variant ("Astra_L" -> model "Astra"; KBA counts every Astra generation as one series); a
/// trailing "_e"/"-e" is the electric version. Two generation names are the KBA series of their own
/// and are mapped explicitly: Zafira D is sold and registered as "Zafira Life", and Saab's groups are
/// named "Modell 9-3"/"Modell 9-5".
///
/// Year: the label's bracketed year is the launch year ("(2008/2013)" = launched 2008, facelift
/// 2013), read as the start year. Body and fuel types are normalized from the German and English
/// label wording ("Kombi"/"Estate"/"Caravan"/"Sports Tourer" -> "Kombi", "Autogas (LPG)" -> "LPG",
/// "Erdgas (CNG)"/"Compressed Natural Gas" -> "CNG"). Labels are whitespace-collapsed (three real
/// ones end in a stray "\r\n"). Should a record ever come without a label, the <c>auto_typ</c> plus
/// the filename tokens ("deu_ov_grandland_suv_2024", "..._cargo_e_life_50") stand in for it.
/// </summary>
public static class IfzBerlinLabelParser
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex GenerationLetter = new(@"^[A-Z]$", RegexOptions.Compiled);
    private static readonly Regex YearInFileName = new(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex EnglishDoors = new(@"(?<!\d)(\d)\s*-?\s*(?:doors?|porte)(?![\p{L}])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly (Regex Pattern, string BodyType)[] BodyTypes =
    [
        (WholeWord(@"Kombi|Caravan|Sports\s+Tourer|SportCombi|Estate|Station"), "Kombi"),
        (WholeWord(@"Cabrio|Convertible|Twin\s+Top"), "Cabriolet"),
        (WholeWord("Coup[eé]"), "Coupé"),
        (WholeWord("SUV|CUV"), "SUV"),
        (WholeWord("Hatchback"), "Schrägheck"),
        (WholeWord(@"Crew\s+Cab"), "Doppelkabine"),
        (WholeWord("Cargo|Van"), "Kastenwagen"),
        (WholeWord("Combi"), "Kleinbus"),
        (WholeWord("Tour"), "Hochdachkombi")
    ];

    // Hydrogen first ("Vivaro_C Hydrogen electric Fuel Cell"), then hybrid, then electric.
    private static readonly (Regex Pattern, string FuelType)[] FuelTypes =
    [
        (WholeWord(@"Hydrogen|HydroGen4|Fuel\s+Cell|Wasserstoff"), "Wasserstoff"),
        (WholeWord(@"Hybrid|Hybrid\s*4"), "Hybrid"),
        (WholeWord("electric|Elektro"), "Elektro"),
        (WholeWord(@"CNG|Erdgas|Compressed\s+Natural\s+Gas"), "CNG"),
        (WholeWord("LPG|Autogas"), "LPG")
    ];

    public static ParsedModelInfo Parse(string autoTyp, string? detailText, string detailFile, string languageCode)
    {
        var (modelName, _, isElectric, typeBody) = FromAutoTyp(autoTyp);

        var label = Whitespace.Replace(detailText ?? string.Empty, " ").Trim();
        var fileTokens = Path.GetFileName(detailFile).Replace('_', ' ');
        var hasLabel = label.Length > 0;
        if (!hasLabel)
        {
            label = Whitespace.Replace(autoTyp.Replace('_', ' '), " ").Trim();
        }

        // Only a missing label falls back to the filename: in regular filenames a lone "e" is just as
        // often the generation letter ("german_opelvauxhall_corsa_e_1" is a petrol Corsa E).
        var attributeText = hasLabel ? label : $"{label} {fileTokens}";

        var years = ModelYearRangeTextHelper.Extract(label, singleYearIsStartYear: true);
        if (years is { From: null, To: null } && YearInFileName.Match(detailFile) is { Success: true } fileYear)
        {
            years = new YearRange(int.Parse(fileYear.Value), null);
        }

        var fuelType = FirstMatch(FuelTypes, attributeText)
            ?? (isElectric || (!hasLabel && Regex.IsMatch(fileTokens, @"(?<![\p{L}\p{N}])e(?![\p{L}\p{N}])")) ? "Elektro" : null);

        var hasYear = years.From is not null || years.To is not null;
        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: label,
            BodyType: FirstMatch(BodyTypes, attributeText) ?? typeBody,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(label)
                ?? (EnglishDoors.Match(label) is { Success: true } doors ? int.Parse(doors.Groups[1].Value) : null),
            FuelType: fuelType,
            LanguageCode: languageCode,
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed : hasYear ? ParseConfidence.High : ParseConfidence.Heuristic);
    }

    /// <summary>Model name, generation letter, electric flag and body type from an <c>auto_typ</c>.</summary>
    internal static (string? ModelName, string? Generation, bool IsElectric, string? BodyType) FromAutoTyp(string autoTyp)
    {
        var tokens = autoTyp.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // Brand/collection prefixes ("Opel_GT", "OPEL_Speedster", Saab's "Modell_9-3").
        if (tokens.Count > 1 && (tokens[0].Equals("Opel", StringComparison.OrdinalIgnoreCase) || tokens[0] == "Modell"))
        {
            tokens.RemoveAt(0);
        }

        // Saab's "9-3"/"9-5": the hyphen belongs to the name.
        if (tokens.Count == 2 && tokens.All(t => t.All(char.IsDigit)))
        {
            return ($"{tokens[0]}-{tokens[1]}", null, false, null);
        }

        var isElectric = false;
        if (tokens.Count > 1 && tokens[^1] == "e")
        {
            tokens.RemoveAt(tokens.Count - 1);
            isElectric = true;

            // "Opel_Rocks_e" and "Ampera-e" are names in their own right, not the electric version of
            // a "Rocks"/"Ampera".
            if (tokens is ["Rocks"] or ["Ampera"])
            {
                return ($"{tokens[0]}-e", null, true, null);
            }
        }

        string? bodyType = null;
        if (tokens.Count > 1 && FirstMatch(BodyTypes, tokens[^1]) is { } body)
        {
            bodyType = body;
            tokens.RemoveAt(tokens.Count - 1);
        }

        string? generation = null;
        if (tokens.Count > 1 && GenerationLetter.IsMatch(tokens[^1]))
        {
            generation = tokens[^1];
            tokens.RemoveAt(tokens.Count - 1);
        }

        var model = string.Join(' ', tokens);
        if (model.Equals("Zafira", StringComparison.OrdinalIgnoreCase) && generation == "D")
        {
            model = "Zafira Life";
        }

        return (model.Length == 0 ? null : model, generation, isElectric, bodyType);
    }

    private static string? FirstMatch((Regex Pattern, string Value)[] vocabulary, string text) =>
        vocabulary.FirstOrDefault(v => v.Pattern.IsMatch(text)).Value;

    private static Regex WholeWord(string alternatives) =>
        new($@"(?<![\p{{L}}\p{{N}}])(?:{alternatives})(?![\p{{L}}\p{{N}}])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
