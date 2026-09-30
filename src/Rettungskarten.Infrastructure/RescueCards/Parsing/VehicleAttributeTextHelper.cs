using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.RescueCards.Parsing;

/// <summary>
/// Shared helper for pulling a body type/fuel type out of free-text titles or headers against a known
/// vocabulary (Porsche's page-header text, Škoda's model-page link titles) - neither source publishes
/// these as a separate field. Takes the *last* vocabulary match in the text rather than the first:
/// a body-style word can also appear earlier as part of the model designation itself (e.g. "Boxster
/// Spyder" names the model, but a later "Cabriolet" in the same text is the actual body type; "Škoda
/// Kodiaq iV SUV 2024 5d hybrid" names an "iV" trim before its actual fuel type, "hybrid", at the end).
/// Returns null - never a guess - when no vocabulary word is present, so an unrecognized future body
/// style/fuel type shows up as missing data rather than as a silently wrong value.
/// </summary>
public static class VehicleAttributeTextHelper
{
    /// <summary>
    /// Body styles as they appear in German and English rescue-sheet labels/filenames across brands -
    /// a default for sources whose own text has no brand-specific wording. Brands with their own terms
    /// (Porsche, Škoda) keep their own lists.
    /// </summary>
    public static IReadOnlyList<string> CommonBodyTypes { get; } =
    [
        "Limousine", "Stufenheck", "Schrägheck", "Fließheck", "Kombi", "Sports Tourer", "Sportstourer",
        "Tourer", "Touring", "Variant", "Avant", "Estate", "Wagon", "Station Wagon", "Shooting Brake",
        "Coupé", "Coupe", "Cabriolet", "Cabrio", "Convertible", "Roadster", "Spider", "Spyder", "Targa",
        "Hatchback", "Sedan", "Saloon", "Fastback", "Liftback", "Crossover", "SUV", "Gran Coupé",
        "Van", "Minivan", "MPV", "Großraumlimousine", "Kastenwagen", "Kasten", "Panel Van", "Kombi Van",
        "Kleinbus", "Bus", "Transporter", "Pritsche", "Fahrgestell", "Chassis Cab", "Doppelkabine",
        "Pick-up", "Pickup", "Pick Up"
    ];

    /// <summary>
    /// Fuel types/drivetrains across brands, including the brand-specific badges that name the
    /// drivetrain on their own ("e-HYBRID", "4xe", "DM-i", "Natural Power", "BiFuel").
    /// </summary>
    public static IReadOnlyList<string> CommonFuelTypes { get; } =
    [
        "Plug-in-Hybrid", "Plug-in Hybrid", "Plug-In-Hybrid", "PHEV", "Mild-Hybrid", "Mild Hybrid", "MHEV",
        "Vollhybrid", "Full Hybrid", "HEV", "Hybrid-Electric", "Hybrid", "e-HYBRID", "eHYBRID", "4xe",
        "DM-i", "E-Tech", "e:HEV", "Elektro", "Electric", "Elektrisch", "BEV", "EV", "e-tron",
        "Wasserstoff", "Brennstoffzelle", "Fuel Cell", "FCEV", "Hydrogen", "Benzin", "Petrol", "Gasoline",
        "Diesel", "CNG", "Erdgas", "Natural Power", "LPG", "Autogas", "BiFuel", "Bi-Fuel", "Ethanol", "E85"
    ];

    public static string? ExtractLastVocabularyMatch(string text, IReadOnlyList<string> vocabulary)
    {
        // Longer/more specific phrases must be tried before a shorter one that's a substring of it
        // (e.g. "PHEV HYBRID" before bare "Hybrid") so a compound term isn't preempted by its own tail.
        var pattern = string.Join('|', vocabulary.OrderByDescending(v => v.Length).Select(Regex.Escape));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
        return matches.Count > 0 ? matches[^1].Value : null;
    }

    /// <summary>
    /// Like <see cref="ExtractLastVocabularyMatch"/>, but only matches whole words (letters/digits on
    /// neither side; underscores, hyphens and punctuation count as separators). Required for the
    /// short entries of the common vocabularies - a bare substring search finds "EV" in "Levante" and
    /// "Chevrolet", "Van" in "Caravan", "Bus" in "Busan".
    /// </summary>
    public static string? ExtractLastWordMatch(string text, IReadOnlyList<string> vocabulary)
    {
        var matches = WholeWordPattern(vocabulary).Matches(text);
        return matches.Count > 0 ? matches[^1].Value : null;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<string>, Regex> WholeWordPatterns = new();

    /// <summary>One compiled whole-word alternation per vocabulary instance (cached - sources call
    /// this for every label on pages with hundreds of entries).</summary>
    internal static Regex WholeWordPattern(IReadOnlyList<string> vocabulary) =>
        WholeWordPatterns.GetValue(vocabulary, v =>
        {
            var alternatives = string.Join('|', v.OrderByDescending(w => w.Length).Select(Regex.Escape));
            return new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{alternatives})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });

    private static readonly Regex DoorCountPattern = new(
        @"(\d{1,2})\s*-?\s*(?:d\b|T[üu]rer)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Extracts a door count from either the shared "Nd" convention (e.g. "5d") or Škoda's own
    /// German "N-Türer" phrasing (e.g. "3-Türer").</summary>
    public static int? ExtractDoors(string text)
    {
        var match = DoorCountPattern.Match(text);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
