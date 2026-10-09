using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Matching;

/// <summary>
/// Canonical + alias spellings for each brand as they may appear in KBA statistics files, which use
/// their own inconsistent labels (e.g. "SKODA" without the caron, "VW" instead of "VOLKSWAGEN",
/// "MERCEDES" instead of "MERCEDES-BENZ", "MG ROEWE" for today's SAIC-owned MG). A brand not listed in
/// <see cref="Aliases"/> is matched by its upper-cased enum name.
///
/// A sub-brand also matches every alias of its parent brand (<see cref="BrandGroups.ParentBrandOf"/>),
/// because KBA's FZ12 counts those vehicles under the parent's name. Cupra is the case this was found
/// with: KBA doesn't track it as a brand at all - every Cupra model (Formentor, Born, Ateca, Leon,
/// Tavascan, Terramar) is counted under "SEAT" (confirmed against a real FZ12 file: all 6 Cupra model
/// names have a matching "SEAT {model}" row, and "CUPRA" does not appear anywhere in the file). The
/// same holds for Mercedes-AMG/-EQ/Maybach (counted as "MERCEDES", e.g. "MERCEDES AMG GT") and Fiat
/// Professional/Abarth (counted as "FIAT"). Without this every such card matched zero stock rows and
/// fell back to BundlePriority.Unknown regardless of how common the model actually is.
/// </summary>
public static class BrandNames
{
    private static readonly Dictionary<Brand, string[]> Aliases = new()
    {
        [Brand.VW] = ["VW", "VOLKSWAGEN"],
        [Brand.Skoda] = ["SKODA", "ŠKODA"],
        [Brand.MercedesBenz] = ["MERCEDES", "MERCEDES-BENZ", "MERCEDES BENZ"],
        [Brand.MercedesAmg] = ["MERCEDES-AMG"],
        [Brand.MercedesEq] = ["MERCEDES-EQ"],
        [Brand.RollsRoyce] = ["ROLLS ROYCE", "ROLLS-ROYCE"],
        [Brand.Citroen] = ["CITROEN", "CITROËN"],
        [Brand.AlfaRomeo] = ["ALFA ROMEO"],
        [Brand.LandRover] = ["LAND ROVER"],
        [Brand.MG] = ["MG ROEWE", "MG"],
        // KGM is SsangYong renamed (2023) - FZ12 lists the same models under both names across years.
        [Brand.KGM] = ["KGM", "SSANGYONG"],
        [Brand.FiatProfessional] = ["FIAT PROFESSIONAL"]
    };

    /// <summary>
    /// Every multi-word brand label that KBA's FZ12 puts in front of a model name in its single
    /// combined "Modellreihe" column (e.g. "ALFA ROMEO GIULIA", "LAND ROVER RANGE ROVER SPORT") -
    /// splitting that column at the first space would otherwise turn "ALFA" into the brand and
    /// "ROMEO GIULIA" into the model. Includes brands this tool has no source for (Aston Martin, Lynk &amp;
    /// Co, the old MG Rover) so their rows are still split correctly and can't be mistaken for anything
    /// else. Checked longest-first by the parser.
    /// </summary>
    public static IReadOnlyList<string> MultiWordKbaBrandLabels { get; } =
    [
        "ALFA ROMEO", "ASTON MARTIN", "LAND ROVER", "LYNK & CO", "MG ROEWE", "MG ROVER", "ROLLS ROYCE"
    ];

    public static bool Matches(Brand brand, string brandLabel)
    {
        var normalized = ModelNameNormalizer.Normalize(brandLabel);
        if (OwnAliases(brand).Any(alias => ModelNameNormalizer.Normalize(alias) == normalized))
        {
            return true;
        }

        return BrandGroups.ParentBrandOf(brand) is { } parent
            && OwnAliases(parent).Any(alias => ModelNameNormalizer.Normalize(alias) == normalized);
    }

    private static string[] OwnAliases(Brand brand) =>
        Aliases.TryGetValue(brand, out var aliases) ? aliases : [brand.ToString().ToUpperInvariant()];
}
