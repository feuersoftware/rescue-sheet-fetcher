using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Config;

/// <summary>
/// Hand-curated list of rescue-card models that KBA's FZ12 doesn't list as a series of their own - too
/// rare, too old or too new (FZ12 lumps those into "SONSTIGE"), not a passenger car at all (FZ12 only
/// counts Pkw), or a special that is deliberately not aliased to its base series. Their cards staying
/// <see cref="BundlePriority.Unknown"/> is expected, so <c>inspect quality</c> doesn't report them; if
/// one of them matches after all, that's reported as a warning instead, so the flag can be reviewed.
/// A <see cref="ModelName"/> of <see cref="ModelAliasConfig.AnyModel"/> ("*") flags the whole brand.
/// <see cref="Reason"/> is for the people maintaining the file; the code doesn't read it.
/// </summary>
public sealed record KbaUnlistedModel(Brand Brand, string ModelName, string Reason);

public sealed record KbaUnlistedModelConfig(IReadOnlyList<KbaUnlistedModel> Models)
{
    public bool Contains(Brand brand, string? modelName)
    {
        var normalized = ModelNameNormalizer.Normalize(modelName ?? string.Empty);
        return Models.Any(m => m.Brand == brand &&
            (m.ModelName == ModelAliasConfig.AnyModel || ModelNameNormalizer.Normalize(m.ModelName) == normalized));
    }

    public static readonly KbaUnlistedModelConfig Empty = new([]);
}
