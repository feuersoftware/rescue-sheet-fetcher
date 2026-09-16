using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Matching;

/// <summary>
/// Canonical + alias spellings for each brand as they may appear in KBA statistics files, which use
/// their own inconsistent labels (e.g. "SKODA" without the caron, "VW" instead of "VOLKSWAGEN").
/// Cupra is a special case: KBA's FZ12 does not track it as a brand at all - every Cupra model
/// (Formentor, Born, Ateca, Leon, Tavascan, Terramar) is counted under "SEAT" instead (confirmed
/// against a real FZ12 file: all 6 Cupra model names have a matching "SEAT {model}" row, and "CUPRA"
/// does not appear anywhere in the file). Without this alias every Cupra card matched zero stock rows
/// and fell back to BundlePriority.Unknown regardless of how common the model actually is.
/// </summary>
public static class BrandNames
{
    private static readonly Dictionary<Brand, string[]> Aliases = new()
    {
        [Brand.VW] = ["VW", "VOLKSWAGEN"],
        [Brand.Audi] = ["AUDI"],
        [Brand.Skoda] = ["SKODA", "ŠKODA"],
        [Brand.Seat] = ["SEAT"],
        [Brand.Cupra] = ["CUPRA", "SEAT"],
        [Brand.Porsche] = ["PORSCHE"],
        [Brand.Bentley] = ["BENTLEY"],
        [Brand.Lamborghini] = ["LAMBORGHINI"]
    };

    public static bool Matches(Brand brand, string brandLabel)
    {
        var normalized = ModelNameNormalizer.Normalize(brandLabel);
        return Aliases[brand].Any(alias => ModelNameNormalizer.Normalize(alias) == normalized);
    }
}
