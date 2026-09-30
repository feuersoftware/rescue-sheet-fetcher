using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.ToyotaGroup;

/// <summary>
/// Parses the <c>data-gt-label</c> texts of Toyota's and Lexus' German rescue-sheet pages. Both are
/// regular enough for a brand-specific parser, and the file names are not (a CMS id suffix
/// "_tcm-17-172152", abbreviations like "RLF"/"RK", no years) - they are only consulted for the
/// drivetrain where the label doesn't state it.
///
/// Toyota: "Model [trim/body] (ChassisCode [body] N-Türer, ab MM/YYYY)", with many real deviations:
/// no brackets ("Hilux Single Cab AN1P (EU, N), ab 06/2015", "RAV4 Plug-in Hybrid, ab 06/2020"), a
/// two-digit year ("ab 09/19", "ab 07/20"), "ab Bj. 01/2022", a range after "ab" ("ab 2004-2009"),
/// no year at all ("GT86 (ZN)"), a market suffix instead of a chassis code ("(LHD, ab 11/2020)",
/// "(10_LHD_1 (EU), ...)"). The chassis code is the first all-caps letters+digits token after the
/// model name ("E15UT(a)" -> "E15UT", "XA5", "ZE1HE"), never an LHD/RHD marker.
///
/// Lexus: "Lexus {Series} {Designation} - ab MM/YYYY" ("Lexus NX 450h+ - ab 09/2021") - the series
/// ("NX") is what KBA counts, the designation's suffix names the drivetrain (h = hybrid, h+ =
/// plug-in hybrid, e = electric).
/// </summary>
public static class ToyotaGroupLabelParser
{
    // Longest first. Marketing names that are more than one word, or that KBA spells differently:
    // KBA's "PRIUS PLUS" would never match "Prius+" (normalization drops the "+", making it "Prius").
    private static readonly (string Label, string ModelName)[] KnownToyotaModels =
    [
        ("Proace City Verso", "Proace City Verso"), ("Proace City", "Proace City"),
        ("Proace Verso", "Proace Verso"), ("Proace Max", "Proace Max"),
        ("Land Cruiser", "Land Cruiser"), ("Urban Cruiser", "Urban Cruiser"),
        ("Corolla Cross", "Corolla Cross"), ("Yaris Cross", "Yaris Cross"),
        ("GR Yaris", "GR Yaris"), ("Aygo X", "Aygo X"), ("Prius+", "Prius Plus")
    ];

    // Drivetrains the label doesn't repeat because the model only exists with one.
    private static readonly Dictionary<string, string> SingleDrivetrainModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Prius"] = "Hybrid",
        ["Prius Plus"] = "Hybrid",
        ["Mirai"] = "Hydrogen",
        ["bZ4X"] = "Electric"
    };

    private static readonly IReadOnlyList<string> ToyotaBodyTypes =
    [
        "Fließheck", "Stufenheck", "Limousine", "Kombi", "Combi", "Touring Sports", "Wagon", "Sedan",
        "Single Cab", "Extra Cab", "Double Cab", "Cabriolet"
    ];

    private static readonly Regex ChassisToken = new(@"^[A-Z][A-Z0-9]*\d[A-Z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex TwoDigitYear = new(@"(?<![\d/.,])(0?[1-9]|1[0-2])/(\d{2})(?![\d/])", RegexOptions.Compiled);
    private static readonly Regex PlugIn = new(@"Plug-?in|(?<![A-Za-z])PHEV(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HybridWord = new(@"(?<![A-Za-z])(?:Hybrid|HV)(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ElectricWord = new(@"(?<![A-Za-z])(?:EV|Elektro|Electric)(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // File names glue the drivetrain onto the model code: "RAV4PHV54", "RK_PRIUSPHV_XW52", "YARISHV10".
    private static readonly Regex FileNamePlugIn = new(@"PHV(?=\d|_|-|\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FileNameHybrid = new(@"HV(?=\d|_|-|\.|$)|Hybrid", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LexusDrivetrain = new(@"(?<![A-Za-z0-9])\d{3}(h\+|h|e)(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LexusYearSeparator = new(@"\s[-–]\s", RegexOptions.Compiled);

    public static ParsedModelInfo ParseToyota(string label, string fileName)
    {
        var text = StripPrefix(label.Trim(), "Toyota");
        var headEnd = text.IndexOfAny(['(', ',']);
        var head = (headEnd >= 0 ? text[..headEnd] : text).Trim();

        var (modelName, headRest) = SplitModelName(head);
        var chassisCode = FindChassisCode(headRest) ?? (headEnd >= 0 ? FindChassisCode(text[headEnd..]) : null);

        var years = ExtractYears(text);
        var bodyType = VehicleAttributeTextHelper.ExtractLastWordMatch(headRest, ToyotaBodyTypes)
            ?? (headEnd >= 0 ? VehicleAttributeTextHelper.ExtractLastWordMatch(text[headEnd..], ToyotaBodyTypes) : null);

        // "Proace Verso/ Proace Verso EV" covers two drivetrains in one sheet - naming either would be wrong.
        var fuelType = head.Contains('/') ? null : ToyotaFuel(text, fileName, modelName);

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: label.Trim(),
            BodyType: bodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(text),
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed
                : years.From is not null || years.To is not null ? ParseConfidence.High : ParseConfidence.Heuristic,
            ChassisCode: chassisCode);
    }

    public static ParsedModelInfo ParseLexus(string label)
    {
        var text = StripPrefix(label.Trim(), "Lexus");
        var separator = LexusYearSeparator.Match(text);
        var designation = (separator.Success ? text[..separator.Index] : text).Trim();
        var series = designation.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();

        var years = ExtractYears(text);
        var drivetrain = LexusDrivetrain.Match(designation);
        var fuelType = drivetrain.Success
            ? drivetrain.Groups[1].Value.ToLowerInvariant() switch
            {
                "h+" => "Plug-in Hybrid",
                "h" => "Hybrid",
                _ => "Electric"
            }
            : HybridWord.IsMatch(designation) ? "Hybrid" : null;

        return new ParsedModelInfo(
            ModelName: series,
            Variant: designation,
            BodyType: VehicleAttributeTextHelper.ExtractLastWordMatch(designation, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: VehicleAttributeTextHelper.ExtractDoors(designation),
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: series is null ? ParseConfidence.Unparsed
                : years.From is not null ? ParseConfidence.High : ParseConfidence.Heuristic);
    }

    private static (string? ModelName, string Remainder) SplitModelName(string head)
    {
        foreach (var (known, modelName) in KnownToyotaModels)
        {
            if (head.StartsWith(known, StringComparison.OrdinalIgnoreCase) &&
                (head.Length == known.Length || !char.IsLetterOrDigit(head[known.Length])))
            {
                return (modelName, head[known.Length..].Trim());
            }
        }

        var words = head.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return (null, string.Empty);
        }

        return (NormalizeCase(words[0]), words.Length > 1 ? words[1] : string.Empty);
    }

    /// <summary>"AYGO" -> "Aygo"; names with digits or mixed case ("RAV4", "C-HR", "bZ4X", "iQ")
    /// stay as Toyota writes them.</summary>
    private static string NormalizeCase(string word) =>
        word.Length > 2 && word.All(char.IsUpper)
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())
            : word;

    private static string? FindChassisCode(string text)
    {
        foreach (var raw in text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.TrimStart('(');
            var bracket = token.IndexOf('(');
            if (bracket > 0)
            {
                token = token[..bracket]; // "E15UT(a)", "XW5P(EU,M)"
            }

            token = token.TrimEnd('.', ',', ';', ':', ')');
            if (ChassisToken.IsMatch(token) &&
                !token.StartsWith("LHD", StringComparison.Ordinal) && !token.StartsWith("RHD", StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    private static YearRange ExtractYears(string text) =>
        ModelYearRangeTextHelper.Extract(
            TwoDigitYear.Replace(text, m => $"{m.Groups[1].Value}/20{m.Groups[2].Value}"),
            singleYearIsStartYear: true);

    private static string? ToyotaFuel(string text, string fileName, string? modelName)
    {
        if (PlugIn.IsMatch(text) || FileNamePlugIn.IsMatch(fileName))
        {
            return "Plug-in Hybrid";
        }

        if (HybridWord.IsMatch(text))
        {
            return "Hybrid";
        }

        if (ElectricWord.IsMatch(text))
        {
            return "Electric";
        }

        if (modelName is not null && SingleDrivetrainModels.TryGetValue(modelName, out var fixedFuel))
        {
            return fixedFuel;
        }

        return FileNameHybrid.IsMatch(fileName) ? "Hybrid" : null;
    }

    private static string StripPrefix(string text, string prefix) =>
        text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ? text[(prefix.Length + 1)..].TrimStart() : text;
}
