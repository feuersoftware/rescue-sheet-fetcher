using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>
/// Parses the link/heading labels of the Stellantis Servicebox rescue-sheet pages (Peugeot, Citroën,
/// DS). The label is far more regular than the PDF filenames, which follow at least five different
/// naming schemes on the same pages ("FAD_208_1PP2_de_DE.pdf", "e308_(1PP5)_2023_de.pdf",
/// "Berlingo_(2CK9)_fourgon_2024_de.pdf", "FAD_C3_1CSC_2024_5d_GD_de.pdf",
/// "DS_N8_FWD_Hatchback_2025_5d_Electric_de_DE.pdf") - the standard filename parser reads e.g. the
/// "1CSC" project code of the last one as the body type. Every label, old and new, has the shape
/// "Model [trim/powertrain words] (PROJECT CODE) [body] YEAR→", e.g. "208 (1PIA) 2012→",
/// "C5 Aircross Hybrid PLUG IN Phev (1CRE) 2025→", "Expert (2PG9) verglaster kastenwagen 2007→",
/// "108 (3-Türer) (1PB1) 2014→":
///
/// - the four-character project code in brackets (digit + letter + two alphanumerics: "1PP2", "2CK9",
///   "1SD3") becomes <see cref="ParsedModelInfo.ChassisCode"/>;
/// - the text before the first bracket is the model, minus powertrain/body/trim words ("Hybrid",
///   "MHEV", "E-TENSE", "SW", "CC", "RXH", "Plus", ...) and the electric prefix Stellantis writes as
///   "e", "e-", "ë-" or "E-" ("e208", "ë-C3", "E-Expert") - so "e208" and "208 Hybrid" both become
///   "208", which is the series KBA counts them under;
/// - "YEAR→" means "built from YEAR" (the arrow is always open-ended on these pages);
/// - the fuel type is read from the label/filename words where they are specific (PHEV, MHEV,
///   hydrogen, electric), otherwise from the fuel pictogram next to the link, whose filename encodes
///   it ("Essence-Diesel_01.png", "Electrique_01.png", "Hybride_1.png", "Hydrogene_5.png"; older rows
///   use GUID-named pictograms that say nothing and yield no fuel type);
/// - body types are stated in German on the pages ("lieferwagen", "verglaster kastenwagen") or by
///   Peugeot's own badge ("SW" = estate, "CC" = coupé-cabriolet) and are normalized to German terms.
/// </summary>
public static class ServiceboxLabelParser
{
    private static readonly Regex ProjectCodeInBrackets = new(@"\(\s*([0-9][A-Z][A-Z0-9]{2})\s*\)", RegexOptions.Compiled);
    private static readonly Regex ProjectCodeToken = new(@"(?<![A-Za-z0-9])([0-9][A-Z][A-Z0-9]{2})(?![A-Za-z0-9])", RegexOptions.Compiled);
    private static readonly Regex FirstYear = new(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex YearToken = new(@"^(?:19|20)\d{2}$", RegexOptions.Compiled);
    // "e208", "ë-C3", "E-Expert", and once with a stray space: "e- SpaceTourer".
    private static readonly Regex ElectricPrefix = new(@"^(?:[eë]-\s*|[eë]|E-\s*)(?=[0-9A-Z])", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingNumber = new(@"^\s*\D{0,2}\d+", RegexOptions.Compiled);

    // Words that describe powertrain, body or trim rather than the model itself. Whole words only,
    // longest first ("SW et Break" before "SW", "Hybrid4" before "Hybrid").
    private static readonly Regex NonModelWords = new(
        @"(?<![\p{L}\p{N}])(?:SW\s+et\s+Break|PLUG[\s-]*IN|Hybrid4|Hybride|Hybrid|MHEV|PHEV|BEV|E-TENSE|[ée]lectrique|Hydrogen|Fuel\s+Cell|FWD|AWD|SW|Break|CC|Coup[eé]|Tourer|RXH|Plus|Origin|First|Venturi)(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly (Regex Pattern, string BodyType)[] BodyTypes =
    [
        (new Regex(@"verglaster\s+kastenwagen|fourgon[\s_]+vitr[eé]", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Verglaster Kastenwagen"),
        (new Regex(@"lieferwagen|kastenwagen|fourgon", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Kastenwagen"),
        (LabelText.WholeWord("SW|Break|Tourer|Estate"), "Kombi"),
        (LabelText.WholeWord("CC"), "Coupé-Cabriolet"),
        (LabelText.WholeWord("Coup[eé]"), "Coupé"),
        (LabelText.WholeWord("Hatchback"), "Schrägheck"),
        (LabelText.WholeWord("SUV"), "SUV")
    ];

    // Checked in this order: the specific hybrid kinds before the generic "Hybrid", and every hybrid
    // before "electric" ("FAD_C3_..._Hybrid_Electric_de.pdf" is a hybrid, not a BEV). MHEV wins over
    // "PLUG IN": Citroën labels its C5 Aircross mild hybrid "Hybrid PLUG IN Mhev" (the file says MHEV).
    private static readonly (Regex Pattern, string FuelType)[] FuelTypes =
    [
        (LabelText.WholeWord("MHEV"), "Mild-Hybrid"),
        (LabelText.WholeWord(@"PHEV|PLUG[\s_-]*IN"), "Plug-in-Hybrid"),
        (LabelText.WholeWord(@"Hydrogen|Hydrog[eè]ne|Fuel[\s_]+Cell"), "Wasserstoff"),
        (LabelText.WholeWord("Hybrid4|Hybride?"), "Hybrid"),
        // DS' "E-TENSE" alone is the battery-electric version ("DS 3 CROSSBACK E-TENSE" is the eDS3
        // - verified on the PDF); the plug-in hybrids are labelled "E-TENSE Hybride" and match above.
        (LabelText.WholeWord(@"BEV|Electric|[ée]lectrique|E[-_]TENSE"), "Elektro")
    ];

    private static readonly (string IconPrefix, string FuelType)[] FuelIcons =
    [
        ("Essence-Diesel", "Benzin/Diesel"),
        ("Electrique", "Elektro"),
        ("Hybride", "Hybrid"),
        ("Hydrogene", "Wasserstoff")
    ];

    private static readonly Regex DoorsInFileName = new(@"(?<![A-Za-z0-9])(\d)(?:d|_Portes)(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="label">The row's link text or the heading above a language table.</param>
    /// <param name="fileName">The PDF filename (a secondary source for year, project code, doors).</param>
    /// <param name="fuelIcon">The fuel pictogram's image filename next to the label, if any.</param>
    /// <param name="languageCode">"DE" or "EN", decided by the caller.</param>
    public static ParsedModelInfo Parse(string label, string fileName, string? fuelIcon, string languageCode)
    {
        var text = Whitespace.Replace(label.Replace('→', ' '), " ").Trim();
        var fileText = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');

        var (modelName, isElectricByPrefix) = ExtractModelName(label);

        var chassisCode = ProjectCodeInBrackets.Match(label) is { Success: true } inLabel
            ? inLabel.Groups[1].Value
            : ProjectCodeToken.Match(fileText) is { Success: true } inFile ? inFile.Groups[1].Value : null;

        // Years are searched after the model part only - Peugeot's "2008"/"3008" are valid years too.
        var bracket = label.IndexOf('(');
        var yearText = bracket >= 0 ? label[bracket..] : LeadingNumber.Replace(label, string.Empty);
        var years = ModelYearRangeTextHelper.Extract(yearText.Replace("→", " - "), singleYearIsStartYear: true);
        if (years is { From: null, To: null } && YearFromFileName(fileName, modelName) is { } fileYear)
        {
            years = new YearRange(fileYear, null);
        }

        var fuelType = LabelText.FirstMatch(FuelTypes, text)
            ?? (isElectricByPrefix ? "Elektro" : null)
            ?? LabelText.FirstMatch(FuelTypes, fileText)
            ?? FuelFromIcon(fuelIcon);

        var doors = VehicleAttributeTextHelper.ExtractDoors(text)
            ?? (DoorsInFileName.Match(fileName) is { Success: true } doorMatch ? int.Parse(doorMatch.Groups[1].Value) : null);

        var hasYear = years.From is not null || years.To is not null;
        var confidence = modelName is null
            ? ParseConfidence.Unparsed
            : hasYear && chassisCode is not null
                ? ParseConfidence.High
                : hasYear || chassisCode is not null ? ParseConfidence.Heuristic : ParseConfidence.Unparsed;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: LabelText.FirstMatch(BodyTypes, text) ?? LabelText.FirstMatch(BodyTypes, fileText),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: doors,
            FuelType: fuelType,
            LanguageCode: languageCode,
            ParseConfidence: confidence,
            ChassisCode: chassisCode);
    }

    internal static (string? ModelName, bool IsElectricByPrefix) ExtractModelName(string label)
    {
        var text = Whitespace.Replace(label, " ").Trim();

        // The model is everything before the first bracket ("208 Hybrid (1PP2) ..."); a label without
        // any bracket ends its model name at the year instead.
        var end = text.IndexOf('(');
        if (end < 0)
        {
            var year = FirstYear.Matches(text).FirstOrDefault(m => m.Index > 0);
            end = year?.Index ?? text.Length;
        }

        var model = text[..end].Trim();
        var isElectric = ElectricPrefix.IsMatch(model);
        model = ElectricPrefix.Replace(model, string.Empty);
        model = Whitespace.Replace(NonModelWords.Replace(model, " "), " ").Trim(' ', '-', '/', '–');

        return (model.Length == 0 ? null : model, isElectric);
    }

    /// <summary>The first year token in the filename that isn't the model name itself (Peugeot's
    /// "2008"/"3008" are also valid years).</summary>
    private static int? YearFromFileName(string fileName, string? modelName)
    {
        var tokens = Path.GetFileNameWithoutExtension(fileName).Split(['_', '(', ')', ' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        var year = tokens.FirstOrDefault(t => YearToken.IsMatch(t) && !string.Equals(t, modelName, StringComparison.OrdinalIgnoreCase));
        return year is null ? null : int.Parse(year);
    }

    private static string? FuelFromIcon(string? fuelIcon)
    {
        if (string.IsNullOrWhiteSpace(fuelIcon))
        {
            return null;
        }

        var iconName = fuelIcon.Split('/')[^1];
        return FuelIcons.FirstOrDefault(i => iconName.StartsWith(i.IconPrefix, StringComparison.OrdinalIgnoreCase)).FuelType;
    }
}
