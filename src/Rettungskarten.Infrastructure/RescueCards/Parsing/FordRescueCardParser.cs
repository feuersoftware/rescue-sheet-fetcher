using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Metadata for Ford's rescue cards, from two kinds of text:
///
/// - <see cref="ParsePortalCard"/>: a card linked by a fordserviceinfo.com vehicle lookup. Newer
///   files follow the standard convention ("Ford_Puma_SUV_2025_5d_Electric_DE.pdf"), older ones are
///   document numbers ("G2210196-deDEU-2.0.pdf", label "2020 Kuga FHEV Rettungskarte") or free names
///   ("Mustang-MachE-de.pdf"). The lookup itself says which vehicle line and model year the card was
///   listed for ("Kuga - TD (CX482 EU)", 2020), which is the fallback for everything the filename
///   doesn't state.
/// - <see cref="ParseCombinedHeader"/>: the first-page header of one model in Ford's combined
///   "EU-rescue-cards-all-carlines" PDF, as PdfPig extracts it - model, variant and dates glued
///   together without spaces ("Focus - LPGTurnier08/2007 – 03/2011 F", "C-MAXGrand MAV
///   (5+2-Sitzer)03/2015 – > C"), which is why the model is found from a vocabulary of Ford model
///   names rather than by splitting on spaces.
///
/// Ford's two-word model names (Transit Custom, Tourneo Connect, Mustang Mach-E, ...) are kept
/// together: KBA lists "TRANSIT CUSTOM" separately from "TRANSIT, TOURNEO".
/// </summary>
public static class FordRescueCardParser
{
    // Longest/most specific first. Matched case-insensitively at the start of the text.
    private static readonly string[] ModelNames =
    [
        "E-Transit Custom", "E-Tourneo Custom", "E-Transit Courier", "E-Tourneo Courier", "E-Transit",
        "Grand Tourneo Connect", "Transit Custom", "Transit Connect", "Transit Courier", "Transit Tourneo",
        "Tourneo Custom", "Tourneo Connect", "Tourneo Courier", "Mustang Mach-E", "Grand C-MAX", "Explorer",
        "Transit", "Tourneo", "C-MAX", "B-MAX", "S-MAX", "Eco Sport", "EcoSport", "Edge", "Fiesta", "Focus",
        "Fusion", "Galaxy", "Ka+", "Kuga", "Mondeo", "Mustang", "Puma", "Ranger", "Streetka", "Capri",
        "Bronco", "GT", "KA"
    ];

    private static readonly Regex YearRange = new(
        @"(?:\d{1,2}\s*/\s*)?((?:19|20)\d{2})\s*[–—-]\s*(?:(?:\d{1,2}\s*/\s*)?((?:19|20)\d{2})|>)", RegexOptions.Compiled);

    private static readonly Regex CombinedFuel = new(@"PHEV|mHEV|HEV|LPG|CNG|E=LECTRIC", RegexOptions.Compiled);

    private static readonly string[] CombinedBodyTypes =
    [
        "Turnier", "Cabrio", "Coupé", "Kastenwagen", "Bus", "Kombi", "Einzelkabine", "Doppelkabine", "SUV", "LMV", "SAV", "MAV"
    ];

    private static readonly Regex PortalFuel = new(
        @"(?<![A-Za-z])(PHEV|FHEV|mHEV|HEV|BEV|Electric|Elektro|Hybrid|FHybrid)(?![A-Za-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnyYear = new(@"(?<!\d)((?:19|20)\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex GermanMarker = new(@"(?<![A-Za-z])(?:DE|de|deDEU|Deutsch|German)(?![A-Za-z])", RegexOptions.Compiled);
    private static readonly Regex EnglishMarker = new(@"(?<![A-Za-z])(?:EN|en|enGB|enGBR|English)(?![A-Za-z])", RegexOptions.Compiled);
    private static readonly Regex VehicleLinePlatform = new(@"\(([A-Z0-9]+)[^)]*\)", RegexOptions.Compiled);

    /// <summary>Parses one portal card. <paramref name="vehicleLine"/> is the lookup's model entry
    /// ("Kuga - TD (CX482 EU)", "Mustang Mach-E - GW"), <paramref name="lookupYear"/> its model year.</summary>
    public static ParsedModelInfo ParsePortalCard(string fileName, string label, string vehicleLine, int lookupYear)
    {
        var chassis = VehicleLinePlatform.Match(vehicleLine) is { Success: true } platform ? platform.Groups[1].Value : null;
        var language = DetectLanguage(fileName, label);

        if (StandardRescueSheetFilenameParser.TryParse(fileName, out var standard) && standard.ModelName is not null)
        {
            var (model, variant) = JoinModelName(standard.ModelName, standard.Variant);
            return standard with
            {
                ModelName = model,
                Variant = variant ?? label,
                LanguageCode = standard.LanguageCode ?? language,
                ChassisCode = chassis
            };
        }

        var lineModel = ModelFromVehicleLine(vehicleLine);
        var text = $"{label} {Path.GetFileNameWithoutExtension(fileName)}";
        var yearMatch = AnyYear.Match(text);
        var fuel = PortalFuel.Match(text);

        return new ParsedModelInfo(
            ModelName: lineModel.Model,
            Variant: label,
            BodyType: VehicleAttributeTextHelper.ExtractLastWordMatch(text, VehicleAttributeTextHelper.CommonBodyTypes),
            BuildYearFrom: yearMatch.Success ? int.Parse(yearMatch.Value) : lookupYear,
            BuildYearTo: null,
            Doors: VehicleAttributeTextHelper.ExtractDoors(fileName),
            FuelType: fuel.Success ? fuel.Value : lineModel.Fuel,
            LanguageCode: language,
            ParseConfidence: ParseConfidence.Heuristic,
            ChassisCode: chassis);
    }

    /// <summary>"Kuga Vignale - TD (CX482 EU)" -> Kuga, "Mustang Mach-E - GW" -> Mustang Mach-E, "Ford GT"
    /// -> GT, "Mondeo Hybrid - NG (CD391 EU)" -> Mondeo (Hybrid).</summary>
    internal static (string Model, string? Fuel) ModelFromVehicleLine(string vehicleLine)
    {
        var name = vehicleLine.Split(" - ")[0].Trim();
        name = Regex.Replace(name, @"^Ford\s+", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+Vignale\b", string.Empty, RegexOptions.IgnoreCase);

        var fuel = Regex.Match(name, @"\s+(Hybrid/PHEV|Hybrid/Energi|Hybrid|Electric|Energi)$", RegexOptions.IgnoreCase);
        return fuel.Success ? (name[..fuel.Index], fuel.Groups[1].Value) : (name, null);
    }

    /// <summary>"Transit" + "Custom" -> "Transit Custom"; any other variant stays the variant.</summary>
    internal static (string Model, string? Variant) JoinModelName(string model, string? variant)
    {
        var full = variant is null ? model : $"{model} {variant}";
        var known = MatchModelName(full);
        if (known is null || known.Length <= model.Length)
        {
            return (model, variant);
        }

        var rest = full[known.Length..].Trim();
        return (known, rest.Length > 0 ? rest : null);
    }

    internal static string DetectLanguage(string fileName, string label)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var text in new[] { name, label })
        {
            var german = GermanMarker.Matches(text).LastOrDefault();
            var english = EnglishMarker.Matches(text).LastOrDefault();
            if (german is not null || english is not null)
            {
                return english is not null && (german is null || english.Index > german.Index) ? "EN" : "DE";
            }
        }

        return "DE"; // looked up for the German market
    }

    /// <summary>Parses one model header of the combined PDF, e.g. "Focus - LPGTurnier08/2007 – 03/2011 F"
    /// (without the leading "FORD "). The trailing letter is the document's alphabetical index.</summary>
    public static ParsedModelInfo ParseCombinedHeader(string header)
    {
        var text = header.Trim();
        var model = MatchModelName(text) ?? Regex.Match(text, @"^[A-Za-z][A-Za-z+\-]*").Value;
        if (model.Length == 0)
        {
            return new ParsedModelInfo(null, text, null, null, null, null, null, "DE", ParseConfidence.Unparsed);
        }

        // The index letter repeats the model's initial ("... 08/2012 – > B" for B-MAX, "Explorer PHEV -
        // HEVE", "Transit CourierT").
        if (text.Length > model.Length && char.ToUpperInvariant(text[^1]) == char.ToUpperInvariant(model[0]))
        {
            text = text[..^1].TrimEnd();
        }

        var rest = text[model.Length..];
        var years = YearRange.Match(rest);
        var fuel = CombinedFuel.Match(rest);
        var body = VehicleAttributeTextHelper.ExtractLastVocabularyMatch(rest, CombinedBodyTypes);

        // KBA counts the seven-seat C-MAX as its own series.
        if (model.Equals("C-MAX", StringComparison.OrdinalIgnoreCase) && rest.Contains("Grand", StringComparison.OrdinalIgnoreCase))
        {
            model = "Grand C-MAX";
        }

        return new ParsedModelInfo(
            ModelName: model,
            Variant: text,
            BodyType: body,
            BuildYearFrom: years.Success ? int.Parse(years.Groups[1].Value) : null,
            BuildYearTo: years.Success && years.Groups[2].Success ? int.Parse(years.Groups[2].Value) : null,
            Doors: VehicleAttributeTextHelper.ExtractDoors(rest),
            FuelType: fuel.Success ? (fuel.Value == "E=LECTRIC" ? "Electric" : fuel.Value) : null,
            LanguageCode: "DE",
            ParseConfidence: years.Success ? ParseConfidence.Heuristic : ParseConfidence.Unparsed);
    }

    /// <summary>The known model name <paramref name="text"/> starts with (in the text's own spelling),
    /// or null. The name must not continue with a lower-case letter - in the glued PDF text the next
    /// word starts upper-case ("FocusTurnier") or with a digit ("KA08/2008").</summary>
    private static string? MatchModelName(string text)
    {
        var found = ModelNames.FirstOrDefault(m =>
            text.StartsWith(m, StringComparison.OrdinalIgnoreCase) &&
            (text.Length == m.Length || !char.IsLower(text[m.Length])));
        return found is null ? null : text[..found.Length];
    }
}
