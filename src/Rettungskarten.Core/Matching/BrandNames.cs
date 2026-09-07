using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Matching;

/// <summary>
/// Canonical + alias spellings for each brand as they may appear in KBA statistics files, which use
/// their own inconsistent labels (e.g. "SKODA" without the caron, "VW" instead of "VOLKSWAGEN").
/// </summary>
public static class BrandNames
{
    private static readonly Dictionary<Brand, string[]> Aliases = new()
    {
        [Brand.VW] = ["VW", "VOLKSWAGEN"],
        [Brand.Audi] = ["AUDI"],
        [Brand.Skoda] = ["SKODA", "ŠKODA"],
        [Brand.Seat] = ["SEAT"],
        [Brand.Cupra] = ["CUPRA"],
        [Brand.Porsche] = ["PORSCHE"]
    };

    public static bool Matches(Brand brand, string brandLabel)
    {
        var normalized = ModelNameNormalizer.Normalize(brandLabel);
        return Aliases[brand].Any(alias => ModelNameNormalizer.Normalize(alias) == normalized);
    }
}
