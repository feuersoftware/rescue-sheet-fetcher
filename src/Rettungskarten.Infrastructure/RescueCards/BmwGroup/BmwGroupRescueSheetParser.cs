using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.BmwGroup;

/// <summary>
/// Turns one entry of the BMW Group AOS rescue-sheet API (see <see cref="BmwGroupRescueCardSource"/>)
/// into <see cref="ParsedModelInfo"/>. The API gives three fields per sheet, each only partly reliable:
///
/// - <c>series.name</c>: for BMW the portal's model grouping ("3-series", "x5", "z4", "i3", "iX") - the
///   most reliable source for the KBA model name, so the BMW model name comes from here ("3er", "X5").
///   The one exception is the electric i-models the portal files under their combustion sibling's
///   series ("I4 G26 BEVE" under "4-series", "iX3 G08 BEVE" under "x3", even "iX3 NA5" under "iX"): when
///   the label names an i-model, that name wins (and model-aliases.json maps it back to the KBA series
///   it is registered under). For MINI the series is just the chassis code ("f56", "u25"), and
///   Rolls-Royce leaves it empty, so those brands' model names come from the label instead.
/// - <c>bodyType.name</c>: the portal's body category ("sedan", "touring", "suv", "convertible",
///   "coupe-compact", "compact-van"; MINI: "coupe", "clubman", "convertible", "countryman"). Too coarse
///   and sometimes wrong for a body type as such - "coupe-compact" lumps together 1er hatchbacks, Gran
///   Coupés and real coupés; MINI's "coupe" is its 3-door hatch; MINI's new 5-door Cooper (F65) is
///   filed under "clubman", the Cullinan (an SUV) under "sedan", the Dawn (a convertible) once under
///   "coupe" and the i8 Roadster (I15) under "coupe-compact". A small chassis-code table
///   (<see cref="BodyTypeByChassisCode"/>) corrects exactly these cases; everything else maps from the
///   category (<see cref="BodyTypeByCategory"/>), or stays null where the category says nothing.
///   The filename's own body token is not used: BMW's newer "VUL-BMW_..." filenames follow the
///   industry convention but get it wrong too often ("M3 Series_G81_Hatchback" is a Touring,
///   "1er Series_F70_Sedan" a hatchback).
/// - <c>name</c>: a free-text label like "2er-Reihe F45 PHEV (ab 09/2014)", "X3 G01 PHEV (ab 112019)",
///   "BMW Z4 G29 (ab - 11/2018)", "5-Series I5 G61 BEV (since 03/2024)" (English wording inside the
///   German list) - the source of the chassis code, the year range and the drivetrain tag. The date
///   part is stripped for <see cref="ParsedModelInfo.Variant"/>, which otherwise keeps the label as-is.
///
/// Doors and a fallback drivetrain come from the "..._2024_5d_GD_de-DE.pdf" part of the newer
/// filenames; the older "de_3er-Reihe-E90.pdf" files state neither, which correctly stays null.
/// </summary>
public static class BmwGroupRescueSheetParser
{
    private static readonly Regex DateParenthetical = new(
        @"\((?=[^()]*(?:19|20)\d{2})[^()]*\)", RegexOptions.Compiled);

    // "ab 112019" (X3 G01 PHEV) - month and year run together without the slash.
    private static readonly Regex GluedMonthYear = new(@"(?<!\d)(0[1-9]|1[0-2])((?:19|20)\d{2})(?!\d)", RegexOptions.Compiled);

    // BMW/MINI chassis codes: one letter + two digits (E90, F45, G20, U11, I01, J05, R56), the Neue
    // Klasse "NA0"/"NA5", optionally several joined by "/" ("G11/G12", "F01 / F02 /F04", "E65/66",
    // the Z3's "E36/7"). Exactly two digits, so the i-model names "i4"/"I5" never match.
    private static readonly Regex BmwChassisCode = new(
        @"(?<![A-Za-z0-9])(?:[EFGIJRU]\d{2}|NA\d)(?!\d)(?:\s*/\s*(?:[EFGIJRU]?\d{1,2})(?!\d))*",
        RegexOptions.Compiled);

    // Rolls-Royce: "RR 6", "RR06", "RR25" - normalized to "RR6"/"RR25".
    private static readonly Regex RollsRoyceChassisCode = new(@"(?<![A-Za-z])RR\s*0*(\d{1,2})(?!\d)", RegexOptions.Compiled);

    private static readonly Regex IModelName = new(
        @"(?<![A-Za-z0-9])[iI](?:X[1-7]?|[3-8])(?![A-Za-z0-9])", RegexOptions.Compiled);

    // Case-sensitive on purpose: "ICE" must not match inside "China"/"Price".
    private static readonly Regex DrivetrainTag = new(
        @"(?<![A-Za-z])(PHEV|BEVE?|FCEV|ICE|Active\s?Hybrid|ActiveE)(?![a-z])", RegexOptions.Compiled);

    private static readonly string[] FuelTagPriority = ["PHEV", "FCEV", "BEV", "Hybrid", "ICE"];

    private static readonly Regex FilenameDoors = new(@"_(\d)d_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MiniModelWord = new(
        @"^(?:MINI\s+)?(Clubman|Countryman|Cabrio|Convertible|Roadster|Coup[ée]|Paceman|Aceman|Cooper)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] RollsRoyceModels = ["Phantom", "Ghost", "Wraith", "Dawn", "Cullinan", "Spectre"];

    private static readonly Dictionary<string, string> BodyTypeByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sedan"] = "Limousine",
        ["touring"] = "Touring",
        ["suv"] = "SUV",
        ["countryman"] = "SUV",
        ["convertible"] = "Cabriolet",
        ["coupe-compact"] = "Coupé",
        ["compact-van"] = "Van",
        ["clubman"] = "Kombi",
        // MINI's "coupe" category is its classic 3-door hatch (R50, R56, F56, F66, J01) - the real
        // MINI Coupé (R58) is corrected by chassis code below.
        ["coupe"] = "Schrägheck",
    };

    /// <summary>Per-brand overrides for the categories above. Rolls-Royce's "coupe" is a real coupé
    /// (Wraith, Phantom Coupé, Spectre), unlike MINI's.</summary>
    private static readonly Dictionary<string, string> RollsRoyceBodyTypeByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["coupe"] = "Coupé",
    };

    /// <summary>
    /// Corrections for chassis codes whose portal category is too coarse or plainly wrong (see the
    /// class comment). A null value means "the category is wrong and there's no better single word":
    /// the Paceman (R61) is a 3-door crossover, neither the hatch its "coupe" category implies nor an SUV.
    /// </summary>
    private static readonly Dictionary<string, string?> BodyTypeByChassisCode = new(StringComparer.OrdinalIgnoreCase)
    {
        // 1er hatchbacks, i3 (all filed under "coupe-compact" / "compact-van")
        ["E81"] = "Schrägheck", ["E87"] = "Schrägheck", ["F20"] = "Schrägheck", ["F21"] = "Schrägheck",
        ["F40"] = "Schrägheck", ["F70"] = "Schrägheck", ["I01"] = "Schrägheck",
        // Gran Coupés (4-/5-door, filed under "coupe-compact" or "sedan")
        ["F44"] = "Gran Coupé", ["F74"] = "Gran Coupé", ["F36"] = "Gran Coupé", ["G26"] = "Gran Coupé",
        ["F06"] = "Gran Coupé", ["F93"] = "Gran Coupé", ["G16"] = "Gran Coupé",
        // Gran Turismo (filed under "sedan")
        ["F07"] = "Gran Turismo", ["F34"] = "Gran Turismo", ["G32"] = "Gran Turismo",
        // Roadsters whose label doesn't say so (the others - "Z4 Roadster E89", "MINI Roadster R59" -
        // are caught by the label check): Z4 G29 (filed under "convertible"), i8 Roadster I15 (filed
        // under "coupe-compact"). Not "E36/7": BMW labels both the Z3 Roadster and the Z3 Coupé with it.
        ["G29"] = "Roadster", ["I15"] = "Roadster",
        // MINI
        ["R58"] = "Coupé", ["R61"] = null,
        ["F55"] = "Schrägheck", ["F65"] = "Schrägheck", // F65: 5-door Cooper, filed under "clubman"
        // Rolls-Royce
        ["RR31"] = "SUV", // Cullinan, filed under "sedan"
        ["RR6"] = "Cabriolet", // Dawn, filed once under "convertible" and once under "coupe"
        ["RR2"] = "Cabriolet",
    };

    public static ParsedModelInfo Parse(Brand brand, string label, string? category, string? series, string key)
    {
        var fileName = HttpDownloadHelper.GetFileName(key);
        var chassisCode = ExtractChassisCode(brand, label);
        var variant = LabelText.Collapse(DateParenthetical.Replace(label, " "));

        var yearText = GluedMonthYear.Replace(label, "$1/$2");
        var years = ModelYearRangeTextHelper.Extract(yearText, singleYearIsStartYear: true);

        var (modelName, modelConfident) = brand switch
        {
            Brand.Mini => MiniModelName(label, series),
            Brand.RollsRoyce => RollsRoyceModelName(variant),
            _ => BmwModelName(label, series, variant),
        };

        var confidence = modelConfident && years.From is not null ? ParseConfidence.High : ParseConfidence.Heuristic;

        return new ParsedModelInfo(
            ModelName: modelName,
            Variant: variant,
            BodyType: ResolveBodyType(brand, label, category, chassisCode),
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: FilenameDoors.Match(fileName) is { Success: true } doors ? int.Parse(doors.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            FuelType: ExtractFuelType(brand, label, series, fileName),
            LanguageCode: "DE",
            ParseConfidence: confidence,
            ChassisCode: chassisCode);
    }

    internal static string? ExtractChassisCode(Brand brand, string label)
    {
        if (brand == Brand.RollsRoyce)
        {
            var rr = RollsRoyceChassisCode.Match(label);
            return rr.Success ? "RR" + int.Parse(rr.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }

        var match = BmwChassisCode.Match(label);
        return match.Success ? Regex.Replace(match.Value, @"\s+", string.Empty).ToUpperInvariant() : null;
    }

    /// <summary>"3-series" -> "3er", "x5" -> "X5", "i3" -> "i3", "iX" -> "iX"; an i-model named in the
    /// label ("I4 G26 BEVE", "7er-Reihe i7 G70 BEV") takes precedence over its combustion sibling's
    /// series. M models (M3, M4er-Reihe, X5M) stay under their series - KBA doesn't list them separately.</summary>
    private static (string? Name, bool Confident) BmwModelName(string label, string? series, string variant)
    {
        var iModel = IModelName.Match(label);
        if (iModel.Success)
        {
            var value = iModel.Value;
            return ("i" + (value.Length > 1 && char.ToUpperInvariant(value[1]) == 'X' ? "X" + value[2..] : value[1..]), true);
        }

        if (!string.IsNullOrWhiteSpace(series))
        {
            var numbered = Regex.Match(series, @"^(\d)-series$", RegexOptions.IgnoreCase);
            if (numbered.Success)
            {
                return (numbered.Groups[1].Value + "er", true);
            }

            if (Regex.IsMatch(series, @"^[xz]\w+$", RegexOptions.IgnoreCase))
            {
                return (series.ToUpperInvariant(), true);
            }

            if (Regex.IsMatch(series, @"^i\w+$", RegexOptions.IgnoreCase))
            {
                return ("i" + series[1..], true); // "i3", "i8", "iX" - the labels there are just "I01", "I20"
            }
        }

        // Unknown series shape (a future portal grouping): the label's first word is the best guess.
        var firstWord = variant.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return (firstWord, false);
    }

    /// <summary>"MINI Countryman U25 ICE" -> "Countryman", "Mini Cooper F65" -> "Cooper"; a label
    /// naming no model word ("MINI F56", "F55", "MINI R53 (Cooper S)") is the classic hatch, "MINI".
    /// KBA only knows one series for the whole brand, so this is for rescuers, not matching.</summary>
    private static (string? Name, bool Confident) MiniModelName(string label, string? series)
    {
        if (string.Equals(series, "e", StringComparison.OrdinalIgnoreCase))
        {
            return ("MINI E", true); // "MINI Coupé E": the 2008 electric field-trial hatch, not a Coupé
        }

        var word = MiniModelWord.Match(label.Trim());
        if (!word.Success)
        {
            return ("MINI", true);
        }

        var name = word.Groups[1].Value;
        return (name.StartsWith("Coup", StringComparison.OrdinalIgnoreCase) ? "Coupé" : Capitalize(name), true);
    }

    private static (string? Name, bool Confident) RollsRoyceModelName(string variant)
    {
        var known = RollsRoyceModels.FirstOrDefault(m => Regex.IsMatch(variant, $@"\b{m}\b", RegexOptions.IgnoreCase));
        if (known is not null)
        {
            return (known, true);
        }

        var withoutBrand = Regex.Replace(variant, @"^Rolls[\s-]*Royce\s*", string.Empty, RegexOptions.IgnoreCase);
        return (withoutBrand.Split([' ', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), false);
    }

    private static string? ResolveBodyType(Brand brand, string label, string? category, string? chassisCode)
    {
        if (chassisCode is not null && BodyTypeByChassisCode.TryGetValue(chassisCode, out var byCode))
        {
            return byCode;
        }

        // The label names the body where BMW's own wording is specific ("E36 Compact", "Z4 Roadster
        // E89", "Phantom Drophead Coupé") - more precise than any category.
        if (Regex.IsMatch(label, @"\bCompact\b", RegexOptions.IgnoreCase))
        {
            return "Compact";
        }

        if (Regex.IsMatch(label, @"\bRoadster\b", RegexOptions.IgnoreCase))
        {
            return "Roadster";
        }

        if (Regex.IsMatch(label, @"\bDrophead\b", RegexOptions.IgnoreCase))
        {
            return "Cabriolet";
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        if (brand == Brand.RollsRoyce && RollsRoyceBodyTypeByCategory.TryGetValue(category, out var rollsRoyce))
        {
            return rollsRoyce;
        }

        return BodyTypeByCategory.GetValueOrDefault(category);
    }

    private static string? ExtractFuelType(Brand brand, string label, string? series, string fileName)
    {
        // Most specific tag wins: "X5 ActiveHybrid F15PHEV" is a plug-in hybrid, not a plain hybrid.
        var tags = DrivetrainTag.Matches(label)
            .Select(m => m.Groups[1].Value switch
            {
                "PHEV" => "PHEV",
                "BEV" or "BEVE" or "ActiveE" => "BEV",
                "FCEV" => "FCEV",
                "ICE" => "ICE",
                _ => "Hybrid", // ActiveHybrid 3/5/7, X6 ActiveHybrid
            })
            .ToHashSet();
        var tag = FuelTagPriority.FirstOrDefault(tags.Contains);
        if (tag is not null)
        {
            return tag;
        }

        if (brand == Brand.Mini && (Regex.IsMatch(label, @"\bCooper SE\b") || string.Equals(series, "e", StringComparison.OrdinalIgnoreCase)))
        {
            return "BEV";
        }

        // Newer filenames: "..._5d_Electric_de-DE.pdf", "..._5d_Hybrid (Electric)_de-DE.pdf",
        // "..._5d_Hybrid_Electric_de-DE.pdf", "..._5d_GD_de-DE.pdf" (GD = gasoline/diesel).
        if (Regex.IsMatch(fileName, @"_Hybrid[\s_(]*Electric", RegexOptions.IgnoreCase))
        {
            return "PHEV";
        }

        if (Regex.IsMatch(fileName, @"_Electric", RegexOptions.IgnoreCase))
        {
            return "BEV";
        }

        return Regex.IsMatch(fileName, @"_GD_", RegexOptions.IgnoreCase) ? "ICE" : null;
    }

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
