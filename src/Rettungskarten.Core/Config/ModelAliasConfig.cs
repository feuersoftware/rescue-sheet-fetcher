using Rettungskarten.Core.Matching;
using Rettungskarten.Core.Models;

namespace Rettungskarten.Core.Config;

/// <summary>
/// Hand-curated overrides for cases where a rescue card's model name doesn't automatically match the
/// KBA FZ12 "Modellreihe" spelling (e.g. "ID.4" vs. "ID 4", "T-Roc" vs. "TROC").
/// </summary>
public sealed record ModelAlias(Brand Brand, string ModelName, string KbaModelSeries);

public sealed record ModelAliasConfig(IReadOnlyList<ModelAlias> Aliases)
{
    public string? FindKbaModelSeries(Brand brand, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var normalized = ModelNameNormalizer.Normalize(modelName);
        return Aliases
            .Where(a => a.Brand == brand && ModelNameNormalizer.Normalize(a.ModelName) == normalized)
            .Select(a => a.KbaModelSeries)
            .FirstOrDefault();
    }

    public static readonly ModelAliasConfig Empty = new([]);
}
