using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.RenaultGroup;

/// <summary>
/// Parses Renault's and Dacia's rescue-sheet links: the visible button label ("CAPTUR 2 - 2021",
/// "MASTER E-TECH (ab 2019)", "Sandero 3 (ab 2021)"), Dacia's <c>title</c> attribute where present
/// ("Rettungsdatenblatt Sandero, 2008 bis 2012" - passed in after a " | " separator) and the PDF's
/// real filename.
///
/// The label is the only field that is present and meaningful for every sheet, so the model name
/// always comes from it: its first word, the generation number/trim after it going into the variant
/// (KBA counts "Clio 1" to "Clio 5" as one series "CLIO"). Numbered model names ("Renault 5",
/// "Renault 19") and the two-word names "Grand Scenic"/"Vel Satis" are kept whole.
///
/// The filenames come in three generations, and only two of them carry vehicle data:
/// - the standard convention, often damaged by later edits ("Renault_Clio 5_2020_5d_LPG_DE",
///   "..._DE_aktuell", "..._DE_ueberarbeitet_fuer_Veroeffentlichung", space- or hyphen-separated
///   "Espace E-Tech Full Hybrid Hatchback 2023 5d Hybrid Electric DE") - so instead of the positional
///   <see cref="StandardRescueSheetFilenameParser"/> this looks for the stable core
///   "[Body] Year Nd Fuel DE" with any of "_", " ", "-" as separator;
/// - the old "renault_rettungsdatenblatt_clio_1_2014.pdf" scheme, whose year is the *publication*
///   year of the sheet (every one of ~40 old sheets says 2014, from Clio 1 to Laguna 3) - only an
///   explicit range or "ab-YYYY" in such a name is trusted ("RDB-Logan-MCV-2007-2013",
///   "kangoo-ZE-ab-2011");
/// - free-form names ("fad Arkana DE", "Rettungskarte_Captur II PHEV_de") that only hint at the fuel.
/// Label years win over filename years; the label's fuel wording wins over the filename's.
/// </summary>
public static class RenaultGroupLabelParser
{
    private static readonly Regex StandardCore = new(
        @"(?:^|[ _-])(?:(?<body>[A-Za-z]+)[ _-]+)?(?<year>(?:19|20)\d{2})[ _-]+(?<doors>\d)d[ _-]+(?<fuel>.+?)[ _-]+(?:DE|EN)(?=$|[ _(.-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExplicitFileNameRange = new(
        @"(?<![0-9])((?:19|20)\d{2})[-_]((?:19|20)\d{2})(?![0-9])", RegexOptions.Compiled);

    private static readonly Regex ExplicitFileNameFrom = new(
        @"(?<![A-Za-z])ab[-_ ]((?:19|20)\d{2})(?![0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Dacia's titles say "2008 bis 2012", which the shared helper would read as "bis 2012" only.
    private static readonly Regex YearBisYear = new(
        @"((?:19|20)\d{2})\s+bis\s+((?:19|20)\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DocumentWords = new(
        @"\b(?:Rettungsdatenblatt|Rettungskarte)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NumberedModel = new(
        @"^(?:Renault|Dacia)\s+(\d{1,2})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] TwoWordModels = ["Grand Scenic", "Vel Satis"];

    private static readonly Regex ZeToken = new(@"(?<![A-Za-z])Z\.?E\.?(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedModelInfo Parse(string label, string fileName, IReadOnlyList<string> brandPrefixes)
    {
        var parts = label.Split(" | ", 2, StringSplitOptions.TrimEntries);
        var primary = parts[0].TrimStart('>', ' ');
        var title = parts.Length > 1 ? DocumentWords.Replace(parts[1], " ").Trim() : null;
        var fileStem = Path.GetFileNameWithoutExtension(fileName);

        var modelName = ExtractModelName(primary, brandPrefixes);
        var standard = StandardCore.Match(fileStem);

        var years = ExtractLabelYears(primary);
        if (years is { From: null, To: null } && title is not null)
        {
            years = ExtractLabelYears(title);
        }

        if (years is { From: null, To: null })
        {
            years = standard.Success
                ? new YearRange(int.Parse(standard.Groups["year"].Value), null)
                : ExtractExplicitFileNameYears(fileStem);
        }

        var bodyType = standard.Success && standard.Groups["body"].Success
            ? VehicleAttributeTextHelper.ExtractLastWordMatch(standard.Groups["body"].Value, VehicleAttributeTextHelper.CommonBodyTypes)
            : null;

        var fuelType = FuelFromLabel(primary)
            ?? FuelFromFileNameKeywords(fileStem)
            ?? (standard.Success ? NormalizeStandardFuel(standard.Groups["fuel"].Value) : null);

        var confidence = modelName is null
            ? ParseConfidence.Unparsed
            : standard.Success ? ParseConfidence.High : ParseConfidence.Heuristic;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: primary,
            BodyType: bodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: standard.Success ? int.Parse(standard.Groups["doors"].Value) : null,
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: confidence);
    }

    internal static string? ExtractModelName(string primaryLabel, IReadOnlyList<string> brandPrefixes)
    {
        var text = primaryLabel.Trim();
        var numbered = NumberedModel.Match(text);
        if (numbered.Success)
        {
            // "RENAULT 5 E-TECH ELEKTRISCH", "Renault 19" - the number is the model, not a generation.
            return $"{TitleCase(text[..text.IndexOf(' ')])} {numbered.Groups[1].Value}";
        }

        foreach (var prefix in brandPrefixes)
        {
            if (text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                text = text[(prefix.Length + 1)..].TrimStart();
            }
        }

        var twoWord = TwoWordModels.FirstOrDefault(m => text.StartsWith(m, StringComparison.OrdinalIgnoreCase) &&
            (text.Length == m.Length || !char.IsLetterOrDigit(text[m.Length])));
        if (twoWord is not null)
        {
            return twoWord;
        }

        var firstWord = text.Split([' ', ',', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(firstWord) ? null : TitleCase(firstWord);
    }

    private static YearRange ExtractLabelYears(string text) =>
        ModelYearRangeTextHelper.Extract(YearBisYear.Replace(text, "$1-$2"), singleYearIsStartYear: true);

    private static YearRange ExtractExplicitFileNameYears(string fileStem)
    {
        var range = ExplicitFileNameRange.Match(fileStem);
        if (range.Success)
        {
            return new YearRange(int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value));
        }

        var from = ExplicitFileNameFrom.Match(fileStem);
        return from.Success ? new YearRange(int.Parse(from.Groups[1].Value), null) : new YearRange(null, null);
    }

    private static string? FuelFromLabel(string label)
    {
        if (Regex.IsMatch(label, @"PLUG-?IN", RegexOptions.IgnoreCase))
        {
            return "Plug-in Hybrid";
        }

        // "SCENIC 4 HYBRID ASSIST" is Renault's 48V mild-hybrid system.
        if (Regex.IsMatch(label, @"MILD[\s-]*HYBRID|HYBRID[\s-]+ASSIST", RegexOptions.IgnoreCase))
        {
            return "Mild Hybrid";
        }

        if (Regex.IsMatch(label, @"\bHYBRID\b", RegexOptions.IgnoreCase))
        {
            return "Hybrid";
        }

        if (Regex.IsMatch(label, @"\b(?:ELEKTRISCH|ELECTRIC)\b", RegexOptions.IgnoreCase) || ZeToken.IsMatch(label))
        {
            return "Electric";
        }

        return Regex.IsMatch(label, @"\bLPG\b", RegexOptions.IgnoreCase) ? "LPG" : null;
    }

    private static string? FuelFromFileNameKeywords(string fileStem)
    {
        var text = fileStem.Replace('_', ' ').Replace('-', ' ');
        if (Regex.IsMatch(text, @"(?<![A-Za-z])PHEV(?![A-Za-z])", RegexOptions.IgnoreCase))
        {
            return "Plug-in Hybrid";
        }

        if (Regex.IsMatch(text, @"(?<![A-Za-z])(?:HEV|Hybrid)(?![A-Za-z])", RegexOptions.IgnoreCase))
        {
            return "Hybrid";
        }

        if (ZeToken.IsMatch(text) || Regex.IsMatch(text, @"(?<![A-Za-z])Electric(?![A-Za-z])", RegexOptions.IgnoreCase))
        {
            return "Electric";
        }

        return Regex.IsMatch(text, @"(?<![A-Za-z])LPG(?![A-Za-z])", RegexOptions.IgnoreCase) ? "LPG" : null;
    }

    /// <summary>The convention's fuel token as Renault writes it: "GD" (gasoline/diesel), "LPG",
    /// "Electric", "Hybrid (Electric)", "Hybrid-Electric".</summary>
    private static string NormalizeStandardFuel(string token)
    {
        if (token.Contains("Hybrid", StringComparison.OrdinalIgnoreCase))
        {
            return "Hybrid";
        }

        if (token.Contains("Electric", StringComparison.OrdinalIgnoreCase))
        {
            return "Electric";
        }

        return token.Equals("GD", StringComparison.OrdinalIgnoreCase) ? "Petrol/Diesel" : token.Trim();
    }

    private static string TitleCase(string word) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant());
}
