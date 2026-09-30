using System.Globalization;
using System.Text.RegularExpressions;
using Rettungskarten.Core.Models;
using Rettungskarten.Infrastructure.Http;
using Rettungskarten.Infrastructure.RescueCards.Parsing;

namespace Rettungskarten.Infrastructure.RescueCards.Stellantis;

/// <summary>
/// Parses the link labels of the German ex-FCA brand sites (fiat.de, jeep.de, alfaromeo.de, lancia.de,
/// abarth.de, dodge.de) - see <see cref="StellantisBrandSiteRescueCardSource"/>. The labels are short
/// and regular - "{Brand} {Model} [{trim/fuel/body/doors}] [{MY} {year range}]", e.g.
/// "Jeep® Grand Cherokee 4xe (PHEV)", "Fiat Tipo 5-Türer", "Tonale Ibrida Plug-in", "Caliber MY 2006-2011",
/// "E-Ducato" - but they mix German, Italian and marketing words, which the generic
/// <see cref="RescueSheetLabelParser"/> doesn't know, and almost none of them states a year. What this
/// adds on top of it:
///
/// - Italian fuel words (Alfa's labels are partly untranslated: "Ibrida", "Elettrica") and "Benziner",
///   with every fuel spelling normalized to one German value (<see cref="NormalizeFuel"/>);
/// - "E-{Model}" (Fiat Professional's "E-Ducato", "E-Scudo", "E-Doblo") read as the electric variant of
///   the model KBA knows as "Ducato"/"Scudo"/"Doblo";
/// - Dodge's "MY 2012" model-year prefix, Alfa's glued "159SW" (SW = Sportwagon, the estate);
/// - trim words that follow the model name but aren't part of what KBA counts ("Giulia Quadrifoglio",
///   "Journey ECO", "695 Tributo Ferrari") end up in <see cref="ParsedModelInfo.Variant"/>, not the name;
/// - where the filename also follows the standard convention (Alfa's Junior sheets,
///   "Alfa_Romeo_JUNIOR_BEV__SUV_2024_5d_Electric_de.pdf"), its year/doors/body fill what the label
///   doesn't state - the label still wins where both have a value (the Junior Ibrida's filename says
///   fuel "FC", which isn't a fuel type);
/// - Jeep's Wrangler filenames carry the platform code ("WRANGLERJL"), kept as the chassis code.
///
/// A sheet whose label says nothing (an empty image link, "hier downloaden") is parsed from its
/// filename instead (<see cref="LabelFromFileName"/>). Year precision is the year only; the dates in
/// Stellantis document codes ("..._DE_01_06_23_T") are document release dates, never build years, and
/// are deliberately not read.
/// </summary>
public static class StellantisRescueSheetLabelParser
{
    /// <summary>Invariant (English) model name for the brand-wide collection PDFs (Fiat's and Abarth's
    /// "ShedaSoccorso" documents), which cover several models in one file.</summary>
    public const string CollectionModelName = "Various Models";

    private static readonly string[] ExtraFuelTypes = ["Benziner", "Ibrida Plug-in", "Ibrida", "Elettrica"];

    private static readonly IReadOnlyList<string> FuelTypes =
        [.. VehicleAttributeTextHelper.CommonFuelTypes, .. ExtraFuelTypes];

    private static readonly IReadOnlyList<string> BodyTypes = [.. VehicleAttributeTextHelper.CommonBodyTypes, "SW"];

    /// <summary>Words after the model name that name a trim/edition, not a different KBA model.</summary>
    private static readonly string[] VariantSuffixes = ["Quadrifoglio", "ECO", "Tributo Ferrari"];

    private static readonly Regex TrademarkSigns = new(@"[®™]", RegexOptions.Compiled);
    private static readonly Regex ModelYearPrefix = new(@"\bMY\s*(?=(?:19|20)\d{2}\b)", RegexOptions.Compiled);
    private static readonly Regex GluedSportwagon = new(@"(?<=\d)(SW)\b", RegexOptions.Compiled);
    private static readonly Regex ElectricModelPrefix = new(@"^E-(?=\p{L})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DoorsPhrase = new(@"\s*\b\d{1,2}\s*-?\s*T[üu]rer\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex WranglerPlatform = new(@"WRANGLER(JL|JK)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CollectionFileName = new(@"Sc?hedaSoccorso", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Stellantis document code: "{brand no}_{project no}_{MODEL}_..." e.g. "70_406_FLAVIA_000.00.000_DE_01_05.12_T",
    // "77_276_NUOVODOBLÒ_000_00_000_DE_01_06_22_T_TE", "66_XXX_ShedaSoccorso_XXX_YY_ZZZ_DE_01_01.12_T".
    private static readonly Regex DocumentCode = new(@"^\d{2,3}_[0-9X]{3}_([^_]+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True for the brand-wide "ShedaSoccorso" (sic - Italian "scheda soccorso", rescue sheet)
    /// collection documents.</summary>
    public static bool IsCollection(string fileName) => CollectionFileName.IsMatch(fileName);

    public static ParsedModelInfo Parse(string label, string fileNameOrUrl, IReadOnlyList<string> brandPrefixes)
    {
        var fileName = HttpDownloadHelper.GetFileName(fileNameOrUrl);
        if (IsCollection(fileName))
        {
            return new ParsedModelInfo(CollectionModelName, null, null, null, null, null, null, "DE", ParseConfidence.Heuristic);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = LabelFromFileName(fileName);
        }

        var text = Normalize(label);
        var years = ModelYearRangeTextHelper.Extract(text, singleYearIsStartYear: true);
        var fuelType = NormalizeFuel(VehicleAttributeTextHelper.ExtractLastWordMatch(text, FuelTypes));
        var bodyType = NormalizeBody(VehicleAttributeTextHelper.ExtractLastWordMatch(text, BodyTypes));
        var doors = VehicleAttributeTextHelper.ExtractDoors(text);

        var modelName = RescueSheetLabelParser.ExtractModelName(
            text, new RescueSheetLabelParser.Options(brandPrefixes, "DE", BodyTypes, FuelTypes));
        if (modelName is not null)
        {
            modelName = DoorsPhrase.Replace(modelName, string.Empty);
            if (ElectricModelPrefix.IsMatch(modelName))
            {
                modelName = ElectricModelPrefix.Replace(modelName, string.Empty);
                fuelType ??= "Elektro";
            }

            // abarth.de's headings shout some names ("Abarth GRANDE PUNTO"); short all-caps tokens are
            // real designations ("GT", "4C") and stay as they are.
            modelName = string.Join(' ', StripVariantSuffixes(modelName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length >= 4 && w.All(char.IsLetter) ? TitleCaseIfUniformCase(w) : w));
            if (modelName.Length == 0)
            {
                modelName = null;
            }
        }

        var parsed = new ParsedModelInfo(
            ModelName: modelName,
            Variant: text,
            BodyType: bodyType,
            BuildYearFrom: years.From,
            BuildYearTo: years.To,
            Doors: doors,
            FuelType: fuelType,
            LanguageCode: "DE",
            ParseConfidence: modelName is null ? ParseConfidence.Unparsed : ParseConfidence.Heuristic,
            ChassisCode: WranglerPlatform.Match(fileName) is { Success: true } platform ? platform.Groups[1].Value.ToUpperInvariant() : null);

        return FillFromStandardFileName(parsed, fileName);
    }

    /// <summary>A label built from the filename alone, for links whose own text says nothing: the model
    /// token of a Stellantis document code ("70_406_FLAVIA_..." -> "Flavia"), otherwise the filename with
    /// underscores as spaces ("lancia_ypsilon_2011_lpg" -> "Lancia Ypsilon 2011 Lpg").</summary>
    public static string LabelFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(fileName));
        var documentCode = DocumentCode.Match(stem);
        var text = documentCode.Success ? documentCode.Groups[1].Value : stem.Replace('_', ' ');
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(TitleCaseIfUniformCase));
    }

    internal static string? NormalizeFuel(string? fuel) => fuel?.ToLowerInvariant() switch
    {
        null => null,
        "phev" or "4xe" or "ibrida plug-in" or "plug-in-hybrid" or "plug-in hybrid" => "Plug-in-Hybrid",
        "ibrida" or "hybrid" or "e-hybrid" or "mhev" or "mild-hybrid" or "mild hybrid" => "Hybrid",
        "elettrica" or "elektro" or "electric" or "bev" or "ev" => "Elektro",
        "benziner" or "benzin" or "petrol" => "Benzin",
        "lpg" or "autogas" => "LPG",
        "natural power" or "cng" or "erdgas" => "Erdgas",
        "diesel" => "Diesel",
        _ => fuel
    };

    private static string? NormalizeBody(string? body) =>
        string.Equals(body, "SW", StringComparison.OrdinalIgnoreCase) ? "Kombi" : body;

    private static string Normalize(string label)
    {
        var text = TrademarkSigns.Replace(label, " ");
        text = ModelYearPrefix.Replace(text, string.Empty);
        text = GluedSportwagon.Replace(text, " $1");
        return Whitespace.Replace(text, " ").Trim();
    }

    private static string StripVariantSuffixes(string modelName)
    {
        foreach (var suffix in VariantSuffixes)
        {
            var index = modelName.IndexOf(" " + suffix, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && (index + suffix.Length + 1 == modelName.Length || modelName[index + suffix.Length + 1] == ' '))
            {
                modelName = modelName[..index];
            }
        }

        return modelName.Trim();
    }

    private static ParsedModelInfo FillFromStandardFileName(ParsedModelInfo parsed, string fileName)
    {
        // "Alfa_Romeo_JUNIOR_..." puts the two-word brand into two tokens; the convention expects one.
        var conventional = fileName.StartsWith("Alfa_Romeo_", StringComparison.OrdinalIgnoreCase)
            ? "AlfaRomeo_" + fileName["Alfa_Romeo_".Length..]
            : fileName;
        if (!StandardRescueSheetFilenameParser.TryParse(conventional, out var fromFileName))
        {
            return parsed;
        }

        return parsed with
        {
            ModelName = parsed.ModelName ?? fromFileName.ModelName,
            BodyType = parsed.BodyType ?? fromFileName.BodyType,
            BuildYearFrom = parsed.BuildYearFrom ?? fromFileName.BuildYearFrom,
            // A single filename year is the launch year of a model still being built (Junior: 2024),
            // not the one year the sheet applies to - only a real range sets an end year.
            BuildYearTo = parsed.BuildYearFrom is null && fromFileName.BuildYearTo != fromFileName.BuildYearFrom
                ? fromFileName.BuildYearTo
                : parsed.BuildYearTo,
            Doors = parsed.Doors ?? fromFileName.Doors,
            FuelType = parsed.FuelType ?? NormalizeFuel(fromFileName.FuelType),
            ParseConfidence = parsed.ModelName is null ? fromFileName.ParseConfidence : parsed.ParseConfidence
        };
    }

    private static string TitleCaseIfUniformCase(string word) =>
        word == word.ToLowerInvariant() || word == word.ToUpperInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())
            : word;
}
